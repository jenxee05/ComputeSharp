﻿using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ComputeSharp.Core.Helpers;
using ComputeSharp.Core.Interop;
using ComputeSharp.Graphics;
using ComputeSharp.Graphics.Buffers.Abstract;
using ComputeSharp.Shaders.Mappings;
using ComputeSharp.Shaders.Translation.Models;
using TerraFX.Interop;

namespace ComputeSharp.Shaders.Translation
{
    /// <inheritdoc cref="ShaderLoader{T}"/>
    internal sealed partial class ShaderLoader<T>
    {
        /// <summary>
        /// A <see langword="delegate"/> that loads all the captured members from a shader instance.
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> to use to run the shader.</param>
        /// <param name="shader">The shader instance to load the data from.</param>
        /// <param name="r0">A reference to the buffer with all the captured <see cref="D3D12_GPU_DESCRIPTOR_HANDLE"/> instances.</param>
        /// <param name="r1">A reference to the buffer to use to store all the captured value types.</param>
        private delegate void DispatchDataLoader(GraphicsDevice device, in T shader, ref D3D12_GPU_DESCRIPTOR_HANDLE r0, ref byte r1);

        /// <summary>
        /// The number of captured graphics resources (buffers).
        /// </summary>
        private int totalResourceCount;

        /// <summary>
        /// The size in bytes of the buffer with the captured variables.
        /// </summary>
        /// <remarks>Initially set to 3 <see cref="int"/> values for X, Y and Z threads counts.</remarks>
        private int totalVariablesByteSize = 3 * sizeof(int);

        /// <summary>
        /// The cached <see cref="DispatchDataLoader"/> instance to load dispatch data before execution.
        /// </summary>
        private DispatchDataLoader? dispatchDataLoader;

        /// <summary>
        /// The <see cref="List{T}"/> of <see cref="ReadableMember"/> instances mapping the captured variables in the current shader.
        /// </summary>
        private readonly List<ReadableMember> capturedMembers = new();

        /// <summary>
        /// Gets a new <see cref="DispatchData"/> instance with all the captured data for the current shader instance.
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> to use to run the shader.</param>
        /// <param name="shader">The input <typeparamref name="T"/> instance representing the compute shader to run.</param>
        /// <param name="x">The number of iterations to run on the X axis.</param>
        /// <param name="y">The number of iterations to run on the Y axis.</param>
        /// <param name="z">The number of iterations to run on the Z axis.</param>
        [Pure]
        public DispatchData GetDispatchData(GraphicsDevice device, in T shader, int x, int y, int z)
        {
            // Resources and variables buffers
            D3D12_GPU_DESCRIPTOR_HANDLE[] resources = ArrayPool<D3D12_GPU_DESCRIPTOR_HANDLE>.Shared.Rent(this.totalResourceCount);
            byte[] variables = ArrayPool<byte>.Shared.Rent(this.totalVariablesByteSize);

            ref D3D12_GPU_DESCRIPTOR_HANDLE r0 = ref MemoryMarshal.GetArrayDataReference(resources);
            ref byte r1 = ref MemoryMarshal.GetArrayDataReference(variables);

            // Set the x, y and z counters
            Unsafe.As<byte, int>(ref r1) = x;
            Unsafe.Add(ref Unsafe.As<byte, int>(ref r1), 1) = y;
            Unsafe.Add(ref Unsafe.As<byte, int>(ref r1), 2) = z;

            // Invoke the dynamic method to extract the captured data
            this.dispatchDataLoader!(device, in shader, ref r0, ref r1);

            return new DispatchData(resources, this.totalResourceCount, variables, this.totalVariablesByteSize);
        }

        /// <summary>
        /// Builds a new <see cref="DispatchDataLoader"/> instance that loads the dispatch data for the current shader type.
        /// </summary>
        private void BuildDispatchDataLoader()
        {
            // The delegate signature is <GraphicsDevice, in T, ref D3D12_GPU_DESCRIPTOR_HANDLE, ref byte, void>.
            //
            // ldarg.0 = GraphicsDevice
            // ldarg.1 = in T shader
            // ldarg.2 = ref D3D12_GPU_DESCRIPTOR_HANDLE r0
            // ldarg.3 = ref byte r1
            this.dispatchDataLoader = DynamicMethod<DispatchDataLoader>.New(il =>
            {
                foreach (ReadableMember member in this.capturedMembers)
                {
                    if (HlslKnownTypes.IsKnownBufferType(member.MemberType))
                    {
                        // Load the offset address into the resource buffers
                        il.Emit(OpCodes.Ldarg_2);

                        if (totalResourceCount > 0)
                        {
                            il.EmitAddOffset<D3D12_GPU_DESCRIPTOR_HANDLE>(this.totalResourceCount);
                        }

                        if (!member.IsStatic) il.Emit(OpCodes.Ldarg_1);
                        il.EmitReadMember(member);

                        // Call NativeObject.ThrowIfDisposed()
                        il.Emit(OpCodes.Dup);
                        il.EmitCall(OpCodes.Callvirt, typeof(NativeObject).GetMethod(
                            nameof(NativeObject.ThrowIfDisposed),
                            BindingFlags.Instance | BindingFlags.NonPublic)!, null);

                        // Call Buffer<T>.ThrowIfDeviceMismatch(GraphicsDevice)
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Ldarg_0);
                        il.EmitCall(OpCodes.Callvirt, member.MemberType.GetMethod(
                            nameof(Buffer<byte>.ThrowIfDeviceMismatch),
                            BindingFlags.Instance | BindingFlags.NonPublic)!, null);

                        // Access Buffer<T>.D3D12GpuDescriptorHandle
                        il.EmitReadMember(member.MemberType.GetField(
                            nameof(Buffer<byte>.D3D12GpuDescriptorHandle),
                            BindingFlags.Instance | BindingFlags.NonPublic)!);
                        il.EmitStoreToAddress(typeof(D3D12_GPU_DESCRIPTOR_HANDLE));

                        this.totalResourceCount++;
                    }
                    else if (HlslKnownTypes.IsKnownScalarType(member.MemberType) ||
                             HlslKnownTypes.IsKnownVectorType(member.MemberType))
                    {
                        // Calculate the right offset with 16-bytes padding (HLSL constant buffer)
                        int size = member.MemberType == typeof(bool)
                            ? sizeof(uint)
                            : Marshal.SizeOf(member.MemberType); // bool is 4 bytes in HLSL

                        if (this.totalVariablesByteSize % 16 > 16 - size)
                        {
                            this.totalVariablesByteSize += 16 - this.totalVariablesByteSize % 16;
                        }

                        // Load the target address into the variables buffer
                        il.Emit(OpCodes.Ldarg_3);
                        il.EmitAddOffset(totalVariablesByteSize);

                        if (!member.IsStatic) il.Emit(OpCodes.Ldarg_1);

                        il.EmitReadMember(member);
                        il.EmitStoreToAddress(member.MemberType);

                        this.totalVariablesByteSize += size;
                    }
                    else ThrowHelper.ThrowArgumentException("Invalid captured member type");
                }

                il.Emit(OpCodes.Ret);
            });
        }
    }
}
