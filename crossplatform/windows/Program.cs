using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SpektraLab;
internal static class Program {
    [STAThread] static void Main(string[] args){
        ApplicationConfiguration.Initialize();
        if(args.Length>0&&args[0]=="--self-test") { try{SelfTest.Run(args[1]);Environment.ExitCode=0;}catch(Exception e){File.WriteAllText(args[1]+".error.txt",e.ToString());Environment.ExitCode=1;}return; }
        if(args.Length>0&&args[0]=="--memory-test") { try{SelfTest.RunMemory(args[1]);Environment.ExitCode=0;}catch(Exception e){File.WriteAllText(args[1]+".error.txt",e.ToString());Environment.ExitCode=1;}return; }
        Application.Run(new EditorForm(args));
    }
}
internal sealed class EditorForm : Form {
    readonly WebView2 web=new(){Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(21,23,22)};
    readonly SemaphoreSlim gate=new(1,1);
    readonly string root=AppContext.BaseDirectory;
    readonly string[] args;
    NativeEngine? engine;
    string sourceName="照片";
    string sourcePath="";
    string importNotice="";
    bool closing;
    public EditorForm(string[] arguments){
        args=arguments;Text="SpektraLab · 胶片实验室";
        var area=Screen.PrimaryScreen!.WorkingArea;Size=new Size((int)(area.Width*.86),(int)(area.Height*.86));MinimumSize=new Size(760,600);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(21,23,22);Controls.Add(web);
        var icon=Path.Combine(root,"app.ico");if(File.Exists(icon))Icon=new Icon(icon);
        Shown+=async(_,_)=>await Initialize();FormClosing+=OnClosing;
    }
    async Task Initialize(){
        try{
            var profile=args.Length>1&&args[0]=="--ui-test"
                ?Path.Combine(args[1],"WebViewProfile")
                :Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpektraLabCrossPlatform","WebView");
            var env=await CoreWebView2Environment.CreateAsync(null,profile);
            await web.EnsureCoreWebView2Async(env);
            var core=web.CoreWebView2;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsStatusBarEnabled=false;
            core.Settings.AreDevToolsEnabled=args.Contains("--devtools");
            core.Settings.IsWebMessageEnabled=true;
            core.SetVirtualHostNameToFolderMapping("spektralab.local",Path.Combine(root,"ui"),CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://spektralab.local/",StringComparison.Ordinal))e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.WebMessageReceived+=OnMessage;
            core.Navigate("https://spektralab.local/index.html");
            if(args.Length>1&&args[0]=="--ui-test")_ = RunUiTest(args[1]);
            if(args.Contains("--devtools"))core.OpenDevToolsWindow();
        }catch(Exception e){MessageBox.Show(this,"无法启动界面。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n"+e.Message,"SpektraLab",MessageBoxButtons.OK,MessageBoxIcon.Error);Close();}
    }
    async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(e.Source!="https://spektralab.local/index.html"||closing)return;
        int id=0;bool acquired=false;
        try{
            using var doc=JsonDocument.Parse(e.WebMessageAsJson);var message=doc.RootElement;id=message.GetProperty("id").GetInt32();
            string action=message.GetProperty("action").GetString()??"";var payload=message.GetProperty("payload").Clone();
            acquired=await gate.WaitAsync(0);if(!acquired)throw new InvalidOperationException("正在处理，请稍候。");
            object? result=action switch{
                "init"=>await Task.Run(()=>{engine??=new NativeEngine(Path.Combine(root,"resources"));return new{catalog=JsonNode.Parse(engine.Catalog),platform="Windows",version=typeof(Program).Assembly.GetName().Version!.ToString(3)};}),
                "open"=>await Open(payload.GetProperty("settings").GetRawText()),
                "render"=>await Task.Run(()=>{RequireEngine().Render(payload.GetProperty("settings").GetRawText());return Frame(false);}),
                "export"=>await Export(payload.GetProperty("format").GetString()??"png"),
                "licenses"=>await File.ReadAllTextAsync(Path.Combine(root,"licenses","THIRD-PARTY-NOTICES.txt")),
                _=>throw new InvalidOperationException("Unknown action")
            };
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,result}));
        }catch(Exception error){if(args.Length>1&&args[0]=="--ui-test")File.AppendAllText(Path.Combine(args[1],"bridge-errors.txt"),error+"\n");if(!web.IsDisposed)web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,error=error.Message}));}
        finally{if(acquired)gate.Release();}
    }
    NativeEngine RequireEngine()=>engine??throw new InvalidOperationException("引擎尚未就绪");
    async Task<object> Open(string settings){
        using var picker=new OpenFileDialog{Title="选择一张照片",Filter="照片|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.dng;*.nef;*.arw;*.cr2;*.cr3;*.raf;*.rw2;*.orf;*.pef;*.srw;*.3fr;*.iiq|所有文件|*.*",CheckFileExists=true,Multiselect=false};
        string path;
        if(args.Length>1&&args[0]=="--ui-test")path=Path.Combine(args[1],"test-scene.png");
        else{if(picker.ShowDialog(this)!=DialogResult.OK)return new{cancelled=true};path=picker.FileName;}
        return await Task.Run(()=>{
            var n=RequireEngine();bool limited=PhotoImport.Open(n,path,settings);
            importNotice=limited?$"当前内存紧张，工作图像已缩小至 {n.Width} × {n.Height}。释放内存后重新导入可获得更大尺寸。":"";
            sourceName=Path.GetFileName(path);sourcePath=Path.GetFullPath(path);return Frame(true);
        });
    }
    object Frame(bool includeOriginal){var n=RequireEngine();return new{image=ImageFiles.Preview(n.Pixels(),n.Width,n.Height),original=includeOriginal?ImageFiles.Preview(n.Pixels(true),n.Width,n.Height):null,width=n.Width,height=n.Height,name=sourceName,elapsed=n.Elapsed,notice=includeOriginal?importNotice:null};}
    async Task<object> Export(string format){
        if(format!="png"&&format!="jpg")throw new InvalidOperationException("不支持的格式");
        var n=RequireEngine();if(n.Width==0)throw new InvalidOperationException("请先导入照片");
        using var picker=new SaveFileDialog{Title="保存胶片作品",FileName=Path.GetFileNameWithoutExtension(sourceName)+"-SpektraLab."+format,DefaultExt=format,Filter=format=="png"?"PNG 图片|*.png":"JPEG 图片|*.jpg",OverwritePrompt=true,AddExtension=true};
        if(picker.ShowDialog(this)!=DialogResult.OK)return new{cancelled=true};
        // The user sees an explicit overwrite prompt, but the source is never a
        // default target. Commit from a sibling temp file, after encoding ends.
        string target=picker.FileName;
        if(Path.GetFullPath(target).Equals(sourcePath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请使用新的文件名，保留原始照片。");
        await Task.Run(()=>ImageFiles.SaveAtomic(n.Pixels(),n.Width,n.Height,target,format));
        return new{name=Path.GetFileName(target)};
    }
    async void OnClosing(object? sender,FormClosingEventArgs e){
        if(closing)return;e.Cancel=true;closing=true;Enabled=false;
        await gate.WaitAsync();await Task.Run(()=>engine?.Dispose());gate.Release();
        web.Dispose();Close();
    }
    async Task RunUiTest(string output){
        try{
            Directory.CreateDirectory(output);
            async Task WaitFor(string expression){for(int i=0;i<300;i++){if(await web.CoreWebView2.ExecuteScriptAsync(expression)=="true")return;await Task.Delay(200);}throw new Exception("UI timeout: "+expression);}
            async Task Capture(string name){await Task.Delay(300);using var stream=File.Create(Path.Combine(output,name));await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
            await WaitFor("typeof ready !== 'undefined' && ready");
            await Capture("windows-empty.png");
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('open').click()");
            await WaitFor("loaded && !locked && document.getElementById('rendered').complete");
            await Capture("windows-editor.png");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-tab=adjust]').click();document.getElementById('brightness').value=0.6;document.getElementById('brightness').dispatchEvent(new Event('input'))");
            await WaitFor("!queued && !rendering && !document.getElementById('export').disabled");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-view=split]').click()");
            await Capture("windows-compare.png");
            MinimumSize=new Size(300,400);ClientSize=new Size((int)(390*DeviceDpi/96.0),(int)(760*DeviceDpi/96.0));
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-tab=stock]').click();document.querySelector('[data-view=after]').click()");
            await Capture("mobile-layout.png");
            string state=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({loaded,ready,width,height,stocks:catalog.films.length,viewportWidth:innerWidth,overflow:document.documentElement.scrollWidth>innerWidth,errors:document.getElementById('status').textContent})");
            File.WriteAllText(Path.Combine(output,"ui-result.json"),JsonSerializer.Deserialize<string>(state));
            if(await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.scrollWidth>innerWidth")=="true")throw new Exception("Mobile layout overflows horizontally");
        }catch(Exception e){File.WriteAllText(Path.Combine(output,"ui-error.txt"),e.ToString());File.WriteAllText(Path.Combine(output,"ui-state.json"),await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({ready,loaded,locked,rendering,status:document.getElementById('status').textContent,toast:document.getElementById('toast').textContent,openDisabled:document.getElementById('open').disabled})"));using var stream=File.Create(Path.Combine(output,"ui-failure.png"));await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);Environment.ExitCode=1;}
        finally{Close();}
    }
}
internal static class ImageFiles {
    public static Bitmap Decode(string path,int edge){
        using var image=Image.FromFile(path,true);
        if((long)image.Width*image.Height>120_000_000)throw new InvalidOperationException("照片超过 1.2 亿像素，请先缩小后导入。");
        if(image.PropertyIdList.Contains(0x112)){
            int orientation=BitConverter.ToUInt16(image.GetPropertyItem(0x112)!.Value!,0);
            var rotations=new Dictionary<int,RotateFlipType>{{2,RotateFlipType.RotateNoneFlipX},{3,RotateFlipType.Rotate180FlipNone},{4,RotateFlipType.Rotate180FlipX},{5,RotateFlipType.Rotate90FlipX},{6,RotateFlipType.Rotate90FlipNone},{7,RotateFlipType.Rotate270FlipX},{8,RotateFlipType.Rotate270FlipNone}};
            if(rotations.TryGetValue(orientation,out var rotate))image.RotateFlip(rotate);
        }
        double scale=Math.Min(1,(double)edge/Math.Max(image.Width,image.Height));
        var output=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(output);g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(image,new Rectangle(0,0,output.Width,output.Height));return output;
    }
    public static int[] Pixels(Bitmap bitmap){var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{int[] p=new int[bitmap.Width*bitmap.Height];for(int y=0;y<bitmap.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,p,y*bitmap.Width,bitmap.Width);return p;}finally{bitmap.UnlockBits(data);}}
    public static Bitmap Bitmap(int[] pixels,int w,int h){var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);var data=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{for(int y=0;y<h;y++)Marshal.Copy(pixels,y*w,data.Scan0+y*data.Stride,w);}finally{b.UnlockBits(data);}return b;}
    public static string Preview(int[] pixels,int w,int h){using var b=Bitmap(pixels,w,h);using var stream=new MemoryStream();b.Save(stream,ImageFormat.Png);return "data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());}
    public static void SaveAtomic(int[] pixels,int w,int h,string path,string format){
        string temp=path+"."+Guid.NewGuid().ToString("N")+".partial";
        try{using(var b=Bitmap(pixels,w,h)){if(format=="jpg"){var codec=ImageCodecInfo.GetImageEncoders().First(x=>x.MimeType=="image/jpeg");using var p=new EncoderParameters(1);p.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,95L);b.Save(temp,codec,p);}else b.Save(temp,ImageFormat.Png);}File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
