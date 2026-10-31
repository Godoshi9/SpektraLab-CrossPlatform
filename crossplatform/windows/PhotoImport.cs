namespace SpektraLab;
internal static class PhotoImport {
    public static bool Open(NativeEngine engine,string path,string settings){
        int limit=engine.PrepareImport();
        bool raster=new[]{".jpg",".jpeg",".png",".bmp",".tif",".tiff"}.Contains(Path.GetExtension(path).ToLowerInvariant());
        while(true){
            try{
                if(raster)OpenRaster(engine,path,settings,limit);
                else engine.OpenRaw(path,settings,limit);
                return limit<4096&&Math.Max(engine.Width,engine.Height)>=limit-1;
            }catch(Exception e) when(e is NativeMemoryException or OutOfMemoryException){
                // Memory can change between admission and GPU allocation. A
                // bounded retry only changes working size, never film effects.
                if(limit<=256)throw new NativeMemoryException(e.Message);
                GC.Collect(2,GCCollectionMode.Optimized,true,false);
                limit=engine.PrepareImport(Math.Max(256,limit/2));
            }
        }
    }
    static void OpenRaster(NativeEngine engine,string path,string settings,int edge){
        using var bitmap=ImageFiles.Decode(path,edge);
        engine.OpenPixels(ImageFiles.Pixels(bitmap),bitmap.Width,bitmap.Height,settings);
    }
}
