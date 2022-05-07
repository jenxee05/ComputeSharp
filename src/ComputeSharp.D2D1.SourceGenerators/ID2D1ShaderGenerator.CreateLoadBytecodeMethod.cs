﻿using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using ComputeSharp.D2D1.Exceptions;
using ComputeSharp.D2D1.Shaders.Translation;
using ComputeSharp.D2D1.SourceGenerators.Models;
using ComputeSharp.SourceGeneration.Extensions;
using ComputeSharp.SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static ComputeSharp.D2D1.SourceGenerators.Diagnostics.DiagnosticDescriptors;

namespace ComputeSharp.D2D1.SourceGenerators;

/// <inheritdoc/>
partial class ID2D1ShaderGenerator
{
    /// <inheritodoc/>
    private static partial class LoadBytecode
    {
        /// <summary>
        /// Extracts the shader profile for the current shader.
        /// </summary>
        /// <param name="structDeclarationSymbol">The input <see cref="INamedTypeSymbol"/> instance to process.</param>
        /// <returns>The shader profile to use to compile the shader, if present.</returns>
        public static D2D1ShaderProfile? GetShaderProfile(INamedTypeSymbol structDeclarationSymbol)
        {
            if (structDeclarationSymbol.TryGetAttributeWithFullMetadataName("ComputeSharp.D2D1.D2DEmbeddedBytecodeAttribute", out AttributeData? attributeData))
            {
                return (D2D1ShaderProfile)attributeData!.ConstructorArguments[0].Value!;
            }

            return null;
        }

        /// <summary>
        /// Extracts the metadata definition for the current shader.
        /// </summary>
        /// <param name="structDeclarationSymbol">The input <see cref="INamedTypeSymbol"/> instance to process.</param>
        /// <param name="inputCount">The number of inputs for the shader.</param>
        /// <returns>Whether the shader only has simple inputs.</returns>
        public static bool IsSimpleInputShader(INamedTypeSymbol structDeclarationSymbol, int inputCount)
        {
            // Build a map of all simple inputs (unmarked inputs default to being complex)
            bool[] simpleInputsMap = new bool[inputCount];

            foreach (AttributeData attributeData in structDeclarationSymbol.GetAttributes())
            {
                switch (attributeData.AttributeClass?.GetFullMetadataName())
                {
                    case "ComputeSharp.D2D1.D2DInputSimpleAttribute":
                        simpleInputsMap[(int)attributeData.ConstructorArguments[0].Value!] = true;
                        break;
                }
            }

            return simpleInputsMap.All(static x => x);
        }

        /// <summary>
        /// Gets a <see cref="BlockSyntax"/> instance with the logic to try to get a compiled shader bytecode.
        /// </summary>
        /// <param name="sourceInfo">The source info for the shader to compile.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to cancel the operation, if needed.</param>
        /// <param name="diagnostic">The resulting diagnostic from the processing operation, if any.</param>
        /// <returns>The <see cref="ImmutableArray{T}"/> instance with the compiled shader bytecode.</returns>
        public static unsafe ImmutableArray<byte> GetBytecode(HlslShaderSourceInfo sourceInfo, CancellationToken token, out DiagnosticInfo? diagnostic)
        {
            ImmutableArray<byte> bytecode = ImmutableArray<byte>.Empty;

            // No embedded shader was requested, or there were some errors earlier in the pipeline.
            // In this case, skip the compilation, as diagnostic will be emitted for those anyway.
            // Compiling would just add overhead and result in more errors, as the HLSL would be invalid.
            if (sourceInfo.HasErrors ||
                sourceInfo.ShaderProfile is null)
            {
                diagnostic = null;

                goto End;
            }

            try
            {
                token.ThrowIfCancellationRequested();

                // Compile the shader bytecode
                using ComPtr<ID3DBlob> dxcBlobBytecode = D2D1ShaderCompiler.Compile(
                    sourceInfo.HlslSource.AsSpan(),
                    sourceInfo.ShaderProfile.Value,
                    sourceInfo.IsLinkingSupported);

                token.ThrowIfCancellationRequested();

                byte* buffer = (byte*)dxcBlobBytecode.Get()->GetBufferPointer();
                int length = checked((int)dxcBlobBytecode.Get()->GetBufferSize());

                byte[] array = new ReadOnlySpan<byte>(buffer, length).ToArray();

                bytecode = Unsafe.As<byte[], ImmutableArray<byte>>(ref array);
                diagnostic = null;
            }
            catch (Win32Exception e)
            {
                diagnostic = new DiagnosticInfo(EmbeddedBytecodeFailedWithWin32Exception, e.HResult, FixupExceptionMessage(e.Message));
            }
            catch (FxcCompilationException e)
            {
                diagnostic = new DiagnosticInfo(EmbeddedBytecodeFailedWithDxcCompilationException, FixupExceptionMessage(e.Message));
            }

            End:
            return bytecode;
        }

        /// <summary>
        /// Fixes up an exception message to improve the way it's displayed in VS.
        /// </summary>
        /// <param name="message">The input exception message.</param>
        /// <returns>The updated exception message.</returns>
        private static string FixupExceptionMessage(string message)
        {
            // Add square brackets around error headers
            message = Regex.Replace(message, @"((?:error|warning) \w+):", static m => $"[{m.Groups[1].Value}]:");

            return message.NormalizeToSingleLine();
        }
    }
}