package app.spektralab.mobile;
final class NativeEngine {
    static { System.loadLibrary("spektralab_mobile"); }
    static native long create(String resources);
    static native void destroy(long handle);
    static native String catalog(long handle);
    static native void openPixels(long handle,int[] pixels,int width,int height,String settings);
    static native void openRaw(long handle,String path,int maxEdge,String settings);
    static native void render(long handle,String settings);
    static native int[] pixels(long handle,boolean original);
    static native int width(long handle);
    static native int height(long handle);
    static native double elapsed(long handle);
}
