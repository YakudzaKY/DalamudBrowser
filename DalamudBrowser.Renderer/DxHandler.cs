using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_DRIVER_TYPE;
using static TerraFX.Interop.DirectX.D3D11_CREATE_DEVICE_FLAG;
using static TerraFX.Interop.DirectX.DirectX;

namespace DalamudBrowser.Renderer;

internal static unsafe class DxHandler
{
    private static ID3D11Device* device;

    public static ID3D11Device* Device => device;

    public static bool Initialize(uint adapterLuidLow, int adapterLuidHigh)
    {
        IDXGIFactory1* factory;
        var factoryGuid = typeof(IDXGIFactory1).GUID;
        var hr = CreateDXGIFactory1(&factoryGuid, (void**)&factory);
        if (hr.FAILED)
        {
            Console.Error.WriteLine($"Failed to create DXGI factory: {hr}");
            return false;
        }

        IDXGIAdapter* matchedAdapter = null;
        uint index = 0;
        IDXGIAdapter* adapter;
        while (factory->EnumAdapters(index, &adapter) != DXGI.DXGI_ERROR_NOT_FOUND)
        {
            DXGI_ADAPTER_DESC description;
            adapter->GetDesc(&description);
            if (description.AdapterLuid.LowPart == adapterLuidLow && description.AdapterLuid.HighPart == adapterLuidHigh)
            {
                matchedAdapter = adapter;
                break;
            }

            adapter->Release();
            index++;
        }

        if (matchedAdapter == null)
        {
            factory->Release();
            Console.Error.WriteLine("Could not find the game D3D11 adapter.");
            return false;
        }

        var flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if DEBUG
        flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

        ID3D11Device* createdDevice;
        ID3D11DeviceContext* immediateContext;
        hr = D3D11CreateDevice(
            matchedAdapter,
            D3D_DRIVER_TYPE_UNKNOWN,
            HMODULE.NULL,
            (uint)flags,
            null,
            0,
            D3D11.D3D11_SDK_VERSION,
            &createdDevice,
            null,
            &immediateContext);

        matchedAdapter->Release();
        factory->Release();

        if (hr.FAILED)
        {
            Console.Error.WriteLine($"Failed to create renderer D3D11 device: {hr}");
            return false;
        }

        device = createdDevice;
        immediateContext->Release();
        return true;
    }

    public static void Shutdown()
    {
        if (device == null)
        {
            return;
        }

        device->Release();
        device = null;
    }
}
