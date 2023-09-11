using System;
using ComputeSharp.D2D1.Interop;
using ComputeSharp.D2D1.Shaders.Interop.Buffers;
using ComputeSharp.D2D1.Shaders.Interop.Effects.ResourceManagers;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace ComputeSharp.D2D1.Shaders.Interop.Helpers;

/// <summary>
/// Shared helpers for D2D shader effects of all kinds.
/// </summary>
internal static unsafe partial class D2D1ShaderEffect
{
    /// <summary>
    /// Releases all allocated resource texture managers being used.
    /// </summary>
    /// <param name="resourceTextureDescriptionCount">The number of available resource texture descriptions.</param>
    /// <param name="resourceTextureDescriptions">The buffer with the available resource texture descriptions for the shader.</param>
    /// <param name="resourceTextureManagerBuffer">The resource texture managers to inspect and release.</param>
    public static void ReleaseResourceTextureManagers(
        int resourceTextureDescriptionCount,
        D2D1ResourceTextureDescription* resourceTextureDescriptions,
        ref ResourceTextureManagerBuffer resourceTextureManagerBuffer)
    {
        ReadOnlySpan<D2D1ResourceTextureDescription> d2D1ResourceTextureDescriptionRange = new(resourceTextureDescriptions, resourceTextureDescriptionCount);

        // Use the list of resource texture descriptions to see the indices that might have accepted a resource texture manager.
        // Then, retrieve all of them and release the ones that had been assigned (from one of the property bindings).
        foreach (ref readonly D2D1ResourceTextureDescription resourceTextureDescription in d2D1ResourceTextureDescriptionRange)
        {
            ID2D1ResourceTextureManager* resourceTextureManager = resourceTextureManagerBuffer[resourceTextureDescription.Index];

            if (resourceTextureManager is not null)
            {
                _ = ((IUnknown*)resourceTextureManager)->Release();
            }
        }
    }

    /// <summary>
    /// Sets all resource texture managers when an effect is being prepared for rendering.
    /// </summary>
    /// <param name="resourceTextureDescriptionCount">The number of available resource texture descriptions.</param>
    /// <param name="resourceTextureDescriptions">The buffer with the available resource texture descriptions for the shader.</param>
    /// <param name="resourceTextureManagerBuffer">The resource texture managers in use.</param>
    /// <param name="d2D1Info">The input D2D info (either <see cref="ID2D1DrawInfo"/> or <see cref="ID2D1ComputeInfo"/>.</param>
    /// <param name="setResourceTexture">A callback to set a resource texture on <paramref name="d2D1Info"/>.</param>
    /// <param name="hresult">The current <see cref="HRESULT"/>, to modify in case any operations are performed.</param>
    /// <remarks>
    /// The function pointer callback is used because the API to set a resource texture is not the same between <see cref="ID2D1DrawInfo"/>
    /// and <see cref="ID2D1ComputeInfo"/> (so we can't just use a base interface), and normal delegates increase the binary size footprint.
    /// </remarks>
    public static void SetResourceTextureManagers(
        int resourceTextureDescriptionCount,
        D2D1ResourceTextureDescription* resourceTextureDescriptions,
        ref ResourceTextureManagerBuffer resourceTextureManagerBuffer,
        void* d2D1Info,
#if NET6_0_OR_GREATER
        delegate*<void*, uint, ID2D1ResourceTexture*, int> setResourceTexture,
#else
        void* setResourceTexture,
#endif
        ref int hresult)
    {
        ReadOnlySpan<D2D1ResourceTextureDescription> d2D1ResourceTextureDescriptionRange = new(resourceTextureDescriptions, resourceTextureDescriptionCount);

        foreach (ref readonly D2D1ResourceTextureDescription resourceTextureDescription in d2D1ResourceTextureDescriptionRange)
        {
            using ComPtr<ID2D1ResourceTextureManager> resourceTextureManager = resourceTextureManagerBuffer[resourceTextureDescription.Index];

            // If the current resource texture manager is not set, we cannot render, as there's an unbound resource texture
            if (resourceTextureManager.Get() is null)
            {
                hresult = E.E_NOT_VALID_STATE;

                break;
            }

            using ComPtr<ID2D1ResourceTextureManagerInternal> resourceTextureManagerInternal = default;

            // Get the ID2D1ResourceTextureManagerInternal object
            hresult = resourceTextureManager.CopyTo(resourceTextureManagerInternal.GetAddressOf());

            // This cast should always succeed, as when an input resource texture managers is set it's
            // also checked for ID2D1ResourceTextureManagerInternal, but still validate for good measure.
            if (!Windows.SUCCEEDED(hresult))
            {
                break;
            }

            using ComPtr<ID2D1ResourceTexture> d2D1ResourceTexture = default;

            // Try to get the ID2D1ResourceTexture from the manager
            hresult = resourceTextureManagerInternal.Get()->GetResourceTexture(d2D1ResourceTexture.GetAddressOf());

            if (!Windows.SUCCEEDED(hresult))
            {
                break;
            }

            // Set the ID2D1ResourceTexture object to the current index in the ID2D1DrawInfo object in use
#if NET6_0_OR_GREATER
            hresult = setResourceTexture(
                d2D1Info,
                (uint)resourceTextureDescription.Index,
                d2D1ResourceTexture.Get());
#else
            hresult = ((delegate*<void*, uint, ID2D1ResourceTexture*, int>)setResourceTexture)(
                d2D1Info,
                (uint)resourceTextureDescription.Index,
                d2D1ResourceTexture.Get());
#endif

            if (!Windows.SUCCEEDED(hresult))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Sets the input descriptions for a given effect.
    /// </summary>
    /// <param name="inputDescriptionCount">The number of available input descriptions.</param>
    /// <param name="inputDescriptions">The buffer with the available input descriptions for the shader.</param>
    /// <param name="d2D1RenderInfo">The input <see cref="ID2D1RenderInfo"/> instance to use.</param>
    /// <param name="hresult">The current <see cref="HRESULT"/>, to modify in case any operations are performed.</param>
    public static void SetInputDescriptions(
        int inputDescriptionCount,
        D2D1InputDescription* inputDescriptions,
        ID2D1RenderInfo* d2D1RenderInfo,
        ref int hresult)
    {
        if (inputDescriptionCount == 0)
        {
            return;
        }

        // Process al input descriptions and set them
        for (int i = 0; i < inputDescriptionCount; i++)
        {
            ref D2D1InputDescription inputDescription = ref inputDescriptions[i];

            D2D1_INPUT_DESCRIPTION d2D1InputDescription;
            d2D1InputDescription.filter = (D2D1_FILTER)inputDescription.Filter;
            d2D1InputDescription.levelOfDetailCount = (uint)inputDescription.LevelOfDetailCount;

            hresult = d2D1RenderInfo->SetInputDescription((uint)inputDescription.Index, d2D1InputDescription);

            if (!Windows.SUCCEEDED(hresult))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Sets the output buffer for a given effect.
    /// </summary>
    /// <param name="bufferPrecision">The buffer precision for the resulting output buffer.</param>
    /// <param name="channelDepth">The channel depth for the resulting output buffer.</param>
    /// <param name="d2D1RenderInfo">The input <see cref="ID2D1RenderInfo"/> instance to use.</param>
    /// <param name="hresult">The current <see cref="HRESULT"/>, to modify in case any operations are performed.</param>
    public static void SetOutputBuffer(
        D2D1BufferPrecision bufferPrecision,
        D2D1ChannelDepth channelDepth,
        ID2D1RenderInfo* d2D1RenderInfo,
        ref int hresult)
    {
        // If a custom buffer precision or channel depth is requested, set it
        if (bufferPrecision != D2D1BufferPrecision.Unknown ||
            channelDepth != D2D1ChannelDepth.Default)
        {
            hresult = d2D1RenderInfo->SetOutputBuffer(
                (D2D1_BUFFER_PRECISION)bufferPrecision,
                (D2D1_CHANNEL_DEPTH)channelDepth);
        }
    }
}