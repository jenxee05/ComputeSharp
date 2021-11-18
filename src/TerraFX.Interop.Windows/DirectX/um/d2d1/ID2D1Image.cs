// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from um/d2d1.h in the Windows SDK for Windows 10.0.20348.0
// Original source is Copyright © Microsoft. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;

namespace TerraFX.Interop.DirectX
{
    [Guid("65019F75-8DA2-497C-B32C-DFA34E48EDE6")]
    [NativeTypeName("struct ID2D1Image : ID2D1Resource")]
    [NativeInheritance("ID2D1Resource")]
    internal unsafe partial struct ID2D1Image : ID2D1Image.Interface
    {
        public void** lpVtbl;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [VtblIndex(0)]
        public HRESULT QueryInterface([NativeTypeName("const IID &")] Guid* riid, void** ppvObject)
        {
            return ((delegate* unmanaged[Stdcall]<ID2D1Image*, Guid*, void**, int>)(lpVtbl[0]))((ID2D1Image*)Unsafe.AsPointer(ref this), riid, ppvObject);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [VtblIndex(1)]
        [return: NativeTypeName("ULONG")]
        public uint AddRef()
        {
            return ((delegate* unmanaged[Stdcall]<ID2D1Image*, uint>)(lpVtbl[1]))((ID2D1Image*)Unsafe.AsPointer(ref this));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [VtblIndex(2)]
        [return: NativeTypeName("ULONG")]
        public uint Release()
        {
            return ((delegate* unmanaged[Stdcall]<ID2D1Image*, uint>)(lpVtbl[2]))((ID2D1Image*)Unsafe.AsPointer(ref this));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [VtblIndex(3)]
        public void GetFactory(ID2D1Factory** factory)
        {
            ((delegate* unmanaged[Stdcall]<ID2D1Image*, ID2D1Factory**, void>)(lpVtbl[3]))((ID2D1Image*)Unsafe.AsPointer(ref this), factory);
        }

        public interface Interface : ID2D1Resource.Interface
        {
        }

        internal partial struct Vtbl
        {
            [NativeTypeName("HRESULT (const IID &, void **) __attribute__((stdcall))")]
            public delegate* unmanaged[Stdcall]<ID2D1Image*, Guid*, void**, int> QueryInterface;

            [NativeTypeName("ULONG () __attribute__((stdcall))")]
            public delegate* unmanaged[Stdcall]<ID2D1Image*, uint> AddRef;

            [NativeTypeName("ULONG () __attribute__((stdcall))")]
            public delegate* unmanaged[Stdcall]<ID2D1Image*, uint> Release;

            [NativeTypeName("void (ID2D1Factory **) const __attribute__((nothrow)) __attribute__((stdcall))")]
            public delegate* unmanaged[Stdcall]<ID2D1Image*, ID2D1Factory**, void> GetFactory;
        }
    }
}