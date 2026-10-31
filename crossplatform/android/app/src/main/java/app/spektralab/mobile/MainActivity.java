package app.spektralab.mobile;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.ColorSpace;
import android.graphics.ImageDecoder;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Base64;
import android.webkit.*;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.atomic.AtomicBoolean;

public final class MainActivity extends Activity {
    private WebView web;
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final AtomicBoolean busy=new AtomicBoolean();
    private volatile boolean destroyed;
    private long engine;
    private int pickerId;
    private String pickerSettings="{}",exportFormat="png",sourceName="照片";
    private static final String ORIGIN="https://spektralab.local/";
    @Override public void onCreate(Bundle state){
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(26,29,26));getWindow().setNavigationBarColor(Color.rgb(29,32,30));
        web=new WebView(this);web.setBackgroundColor(Color.rgb(21,23,22));setContentView(web);
        WebSettings s=web.getSettings();s.setJavaScriptEnabled(true);s.setDomStorageEnabled(false);
        s.setAllowFileAccess(false);s.setAllowContentAccess(false);s.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        s.setMediaPlaybackRequiresUserGesture(true);s.setSupportMultipleWindows(false);
        web.setWebViewClient(new WebViewClient(){
            @Override public boolean shouldOverrideUrlLoading(WebView view,WebResourceRequest r){return !r.getUrl().toString().equals(ORIGIN+"index.html");}
            @Override public WebResourceResponse shouldInterceptRequest(WebView view,WebResourceRequest r){
                String url=r.getUrl().toString();
                if(!url.startsWith(ORIGIN))return new WebResourceResponse("text/plain","UTF-8",new ByteArrayInputStream(new byte[0]));
                String path=r.getUrl().getPath().substring(1);
                if(!path.equals("index.html")&&!path.equals("style.css")&&!path.equals("app.js"))return new WebResourceResponse("text/plain","UTF-8",new ByteArrayInputStream(new byte[0]));
                try{return new WebResourceResponse(path.endsWith("css")?"text/css":path.endsWith("js")?"application/javascript":"text/html","UTF-8",getAssets().open(path));}
                catch(IOException e){return new WebResourceResponse("text/plain","UTF-8",new ByteArrayInputStream(new byte[0]));}
            }
        });
        web.addJavascriptInterface(new Bridge(),"Spektra");web.loadUrl(ORIGIN+"index.html");
    }
    private final class Bridge {
        @JavascriptInterface public void postMessage(String text){
            if(destroyed)return;
            int requestId=0;
            try{
                if(text.length()>32768)throw new IllegalArgumentException("Request too large");
                JSONObject message=new JSONObject(text);requestId=message.getInt("id");final int id=requestId;
                String action=message.getString("action");JSONObject payload=message.getJSONObject("payload");
                if(!busy.compareAndSet(false,true)){reply(id,null,"正在处理，请稍候。");return;}
                if(action.equals("open")){
                    pickerId=id;pickerSettings=payload.getJSONObject("settings").toString();
                    runOnUiThread(()->{try{Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);intent.setType("*/*");intent.addCategory(Intent.CATEGORY_OPENABLE);startActivityForResult(intent,100);}catch(Exception e){finish(id,null,e.getMessage());}});return;
                }
                if(action.equals("export")){
                    exportFormat=payload.getString("format");if(!exportFormat.equals("png")&&!exportFormat.equals("jpg"))throw new IllegalArgumentException("Invalid format");
                    pickerId=id;
                    runOnUiThread(()->{try{Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.setType(exportFormat.equals("png")?"image/png":"image/jpeg");intent.addCategory(Intent.CATEGORY_OPENABLE);intent.putExtra(Intent.EXTRA_TITLE,sourceName.replaceFirst("\\.[^.]+$","")+"-SpektraLab."+exportFormat);startActivityForResult(intent,101);}catch(Exception e){finish(id,null,e.getMessage());}});return;
                }
                worker.execute(()->{
                    try{
                        Object result;
                        switch(action){
                            case "init":
                                if(engine==0)engine=NativeEngine.create(resources().getAbsolutePath());
                                JSONObject init=new JSONObject().put("catalog",new JSONObject(NativeEngine.catalog(engine))).put("platform","Android");
                                if(BuildConfig.DEBUG&&getIntent().getBooleanExtra("self_test",false))init.put("sample",selfTest());
                                result=init;break;
                            case "render":requireEngine();NativeEngine.render(engine,payload.getJSONObject("settings").toString());result=frame(false);break;
                            case "licenses":result=readAsset("licenses/THIRD-PARTY-NOTICES.txt");break;
                            default:throw new IllegalArgumentException("Unknown action");
                        }finish(id,result,null);
                    }catch(Throwable e){finish(id,null,error(e));}
                });
            }catch(Throwable e){busy.set(false);reply(requestId,null,error(e));}
        }
    }
    private void requireEngine(){if(engine==0)throw new IllegalStateException("引擎尚未就绪");}
    private static String error(Throwable e){return e instanceof OutOfMemoryError?"内存不足，请选择较小的照片。":e.getMessage()==null?e.getClass().getSimpleName():e.getMessage();}
    private void finish(int id,Object result,String error){busy.set(false);reply(id,result,error);}
    private void reply(int id,Object result,String error){
        try{JSONObject message=new JSONObject().put("id",id);if(error==null)message.put("result",result);else message.put("error",error);
            String script="window.nativeReply("+message+")";runOnUiThread(()->{if(!destroyed)web.evaluateJavascript(script,null);});
        }catch(Exception ignored){}
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);
        if(request!=100&&request!=101)return;int id=pickerId;
        if(result!=RESULT_OK||data==null||data.getData()==null){finish(id,cancelled(),null);return;}
        Uri uri=data.getData();
        worker.execute(()->{try{requireEngine();
            if(request==100){String name=displayName(uri);open(uri,name,pickerSettings);sourceName=name;finish(id,frame(true),null);}
            else{export(uri,exportFormat);finish(id,new JSONObject().put("name",displayName(uri)),null);}
        }catch(Throwable e){finish(id,null,error(e));}});
    }
    private JSONObject cancelled(){try{return new JSONObject().put("cancelled",true);}catch(Exception e){return new JSONObject();}}
    private String displayName(Uri uri){try(Cursor c=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){if(c!=null&&c.moveToFirst())return c.getString(0);}catch(Exception ignored){}return "照片";}
    private void open(Uri uri,String name,String settings)throws Exception{
        String lower=name.toLowerCase(Locale.ROOT);
        boolean raw=lower.matches(".*\\.(dng|nef|arw|cr2|cr3|raf|rw2|orf|pef|srw|3fr|iiq)$");
        if(raw){
            File temp=File.createTempFile("spektralab-raw-",".raw",getCacheDir());
            try(InputStream in=getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(temp)){
                if(in==null)throw new IOException("无法读取照片");byte[] buffer=new byte[65536];int count;long total=0;
                while((count=in.read(buffer))!=-1){total+=count;if(total>160L*1024*1024)throw new IOException("RAW 文件超过 160 MB，请先在电脑上转换。");out.write(buffer,0,count);}
            }catch(Throwable e){temp.delete();throw e;}
            try{NativeEngine.openRaw(engine,temp.getAbsolutePath(),2048,settings);}finally{temp.delete();}
        }else{
            Bitmap image=ImageDecoder.decodeBitmap(ImageDecoder.createSource(getContentResolver(),uri),(decoder,info,source)->{
                int w=info.getSize().getWidth(),h=info.getSize().getHeight();double scale=Math.min(1,2048.0/Math.max(w,h));
                decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);decoder.setTargetColorSpace(ColorSpace.get(ColorSpace.Named.SRGB));
                decoder.setTargetSize(Math.max(1,(int)(w*scale)),Math.max(1,(int)(h*scale)));
            });
            try{int w=image.getWidth(),h=image.getHeight();int[] pixels=new int[w*h];image.getPixels(pixels,0,w,0,0,w,h);NativeEngine.openPixels(engine,pixels,w,h,settings);}finally{image.recycle();}
        }
    }
    private Bitmap bitmap(boolean original){int w=NativeEngine.width(engine),h=NativeEngine.height(engine);return Bitmap.createBitmap(NativeEngine.pixels(engine,original),w,h,Bitmap.Config.ARGB_8888);}
    private String preview(boolean original)throws IOException{Bitmap b=bitmap(original);try(ByteArrayOutputStream out=new ByteArrayOutputStream()){if(!b.compress(Bitmap.CompressFormat.PNG,100,out))throw new IOException("预览编码失败");return "data:image/png;base64,"+Base64.encodeToString(out.toByteArray(),Base64.NO_WRAP);}finally{b.recycle();}}
    private JSONObject frame(boolean original)throws Exception{JSONObject j=new JSONObject().put("image",preview(false)).put("width",NativeEngine.width(engine)).put("height",NativeEngine.height(engine)).put("name",sourceName).put("elapsed",NativeEngine.elapsed(engine));if(original)j.put("original",preview(true));return j;}
    private void export(Uri uri,String format)throws Exception{
        if(NativeEngine.width(engine)==0)throw new IOException("请先导入照片");
        // Encode fully in our cache before opening the provider destination.
        File temp=File.createTempFile("spektralab-export-",".tmp",getCacheDir());Bitmap b=bitmap(false);
        try{
            try(OutputStream out=new FileOutputStream(temp)){if(!b.compress(format.equals("jpg")?Bitmap.CompressFormat.JPEG:Bitmap.CompressFormat.PNG,95,out))throw new IOException("图片编码失败");}
            try(InputStream in=new FileInputStream(temp);OutputStream out=getContentResolver().openOutputStream(uri,"wt")){
                if(out==null)throw new IOException("无法写入选定位置");byte[] buffer=new byte[65536];int n;while((n=in.read(buffer))!=-1)out.write(buffer,0,n);out.flush();
            }
        }finally{b.recycle();temp.delete();}
    }
    private String readAsset(String path)throws IOException{try(InputStream in=getAssets().open(path);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[8192];int n;while((n=in.read(b))!=-1)out.write(b,0,n);return out.toString(StandardCharsets.UTF_8.name());}}
    private File resources()throws IOException{
        String stamp=readAsset("resources-version.txt").trim();if(!stamp.matches("[a-f0-9]{64}"))throw new IOException("资源版本无效");
        File directory=new File(getFilesDir(),"engine-"+stamp);File done=new File(directory,".complete");
        if(!done.isFile()){copyAssets("resources",directory);try(FileOutputStream out=new FileOutputStream(done)){out.write(1);}}
        return directory;
    }
    private JSONObject selfTest()throws Exception{
        int w=640,h=420;int[] pixels=new int[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=Color.rgb(x*255/(w-1),y*255/(h-1),80);
        String settings="{\"film_stock\":\"kodak_portra_400\",\"print_stock\":\"kodak_portra_endura\",\"scan_film\":false,\"grain_active\":false,\"halation_active\":true,\"glare_active\":false,\"print_exposure\":1}";
        NativeEngine.openPixels(engine,pixels,w,h,settings);
        if(!java.util.Arrays.equals(pixels,NativeEngine.pixels(engine,true)))throw new Exception("Original pixel mismatch");
        int[] before=NativeEngine.pixels(engine,false);
        NativeEngine.render(engine,settings.replace("\"print_exposure\":1","\"print_exposure\":0.5"));
        if(java.util.Arrays.equals(before,NativeEngine.pixels(engine,false)))throw new Exception("Edit did not affect pixels");
        NativeEngine.render(engine,settings);
        if(!java.util.Arrays.equals(before,NativeEngine.pixels(engine,false)))throw new Exception("Deterministic restore mismatch");
        File directory=new File(getExternalFilesDir(null),"validation");directory.mkdirs();
        File png=new File(directory,"rendered.png");Bitmap b=bitmap(false);
        try(OutputStream out=new FileOutputStream(png)){b.compress(Bitmap.CompressFormat.PNG,100,out);}finally{b.recycle();}
        Bitmap decoded=ImageDecoder.decodeBitmap(ImageDecoder.createSource(png),(decoder,info,source)->decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE));
        int[] saved=new int[w*h];decoded.getPixels(saved,0,w,0,0,w,h);decoded.recycle();
        if(!java.util.Arrays.equals(before,saved))throw new Exception("PNG export pixel mismatch");
        JSONObject report=new JSONObject().put("passed",true).put("backend","native-vulkan").put("width",w).put("height",h).put("png_pixel_agreement",true).put("brightness_edit",true).put("deterministic_restore",true);
        try(FileOutputStream out=new FileOutputStream(new File(directory,"result.json"))){out.write(report.toString().getBytes(StandardCharsets.UTF_8));}
        sourceName="Android 渲染验证.png";return frame(true).put("settings",new JSONObject(settings));
    }
    private void copyAssets(String source,File target)throws IOException{
        String[] children=getAssets().list(source);
        if(children!=null&&children.length>0){if(!target.isDirectory()&&!target.mkdirs())throw new IOException("无法创建资源目录");for(String child:children)copyAssets(source+"/"+child,new File(target,child));}
        else try(InputStream in=getAssets().open(source);OutputStream out=new FileOutputStream(target)){byte[] buffer=new byte[65536];int n;while((n=in.read(buffer))!=-1)out.write(buffer,0,n);}
    }
    @Override protected void onDestroy(){destroyed=true;web.removeJavascriptInterface("Spektra");web.destroy();worker.execute(()->{if(engine!=0){NativeEngine.destroy(engine);engine=0;}});worker.shutdown();super.onDestroy();}
}
