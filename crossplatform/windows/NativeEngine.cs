using System.Runtime.InteropServices;
namespace SpektraLab;
internal sealed class NativeEngine : IDisposable {
    private const string Dll="spektralab_mobile";
    private IntPtr handle;
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sl_create([MarshalAs(UnmanagedType.LPUTF8Str)]string resources);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void sl_destroy(IntPtr h);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sl_error(IntPtr h);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sl_catalog(IntPtr h);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_prepare_import(IntPtr h,int maximumEdge);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_open_argb(IntPtr h,int[] pixels,int w,int height,[MarshalAs(UnmanagedType.LPUTF8Str)]string settings);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_open_raw(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,int maxEdge,[MarshalAs(UnmanagedType.LPUTF8Str)]string settings);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_render(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string settings);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sl_pixels(IntPtr h,int original);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_width(IntPtr h);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int sl_height(IntPtr h);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern double sl_elapsed(IntPtr h);
    public NativeEngine(string resources){handle=sl_create(resources);if(handle==IntPtr.Zero)Throw(Error);}
    string Error=>Marshal.PtrToStringUTF8(sl_error(handle))??"Native engine failed";
    static void Throw(string error){
        if(error.Contains("commit budget",StringComparison.OrdinalIgnoreCase)||error.Contains("bad_alloc")||error.Contains("VkResult -1)")||error.Contains("VkResult -2)"))throw new NativeMemoryException(error);
        throw new InvalidOperationException(error);
    }
    void Check(int result){if(result==0)Throw(Error);}
    public string Catalog=>Marshal.PtrToStringUTF8(sl_catalog(handle))??"{}";
    public int Width=>sl_width(handle);public int Height=>sl_height(handle);public double Elapsed=>sl_elapsed(handle);
    public void OpenPixels(int[] pixels,int w,int h,string settings)=>Check(sl_open_argb(handle,pixels,w,h,settings));
    public int PrepareImport(int maximumEdge=4096){int edge=sl_prepare_import(handle,maximumEdge);Check(edge);return edge;}
    public void OpenRaw(string path,string settings,int maximumEdge=4096)=>Check(sl_open_raw(handle,path,maximumEdge,settings));
    public void Render(string settings)=>Check(sl_render(handle,settings));
    public int[] Pixels(bool original=false){int[] p=new int[checked(Width*Height)];Marshal.Copy(sl_pixels(handle,original?1:0),p,0,p.Length);return p;}
    public void Dispose(){if(handle!=IntPtr.Zero){sl_destroy(handle);handle=IntPtr.Zero;}}
}
internal sealed class NativeMemoryException : Exception {
    public NativeMemoryException(string detail):base("系统可用内存不足，已保留上一张照片。请关闭暂时不用的程序，释放内存后重新导入。",new InvalidOperationException(detail)){}
}
