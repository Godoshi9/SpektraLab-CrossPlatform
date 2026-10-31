using System.Text.Json;
namespace SpektraLab;
internal static class SelfTest {
    public static void Run(string output){
        Directory.CreateDirectory(output);
        using var n=new NativeEngine(Path.Combine(AppContext.BaseDirectory,"resources"));
        using var fixture=new Bitmap(640,420);
        using(var g=Graphics.FromImage(fixture)){g.Clear(Color.FromArgb(175,181,157));g.FillRectangle(Brushes.DarkOliveGreen,0,210,640,210);g.FillEllipse(Brushes.Peru,190,90,230,260);g.FillRectangle(Brushes.Bisque,245,65,100,65);g.FillRectangle(Brushes.IndianRed,60,250,100,120);g.FillRectangle(Brushes.SlateBlue,470,160,105,220);}
        var input=Path.Combine(output,"test-scene.png");fixture.Save(input);
        const string settings="{\"film_stock\":\"kodak_portra_400\",\"print_stock\":\"kodak_portra_endura\",\"scan_film\":false,\"grain_active\":false,\"halation_active\":true,\"glare_active\":false,\"print_exposure\":1}";
        PhotoImport.Open(n,input,settings);
        var rendered=n.Pixels();var png=Path.Combine(output,"rendered.png");
        ImageFiles.SaveAtomic(rendered,n.Width,n.Height,png,"png");
        using var read=new Bitmap(png);
        if(!ImageFiles.Pixels(read).SequenceEqual(rendered))throw new Exception("PNG export differs from native preview pixels");
        var jpg=Path.Combine(output,"rendered.jpg");ImageFiles.SaveAtomic(rendered,n.Width,n.Height,jpg,"jpg");
        using var readJpg=new Bitmap(jpg);if(readJpg.Width!=n.Width||readJpg.Height!=n.Height)throw new Exception("JPEG dimensions differ");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,backend="native-vulkan",width=n.Width,height=n.Height,elapsed_ms=n.Elapsed,png_pixel_agreement=true,jpeg_decoded=true}));
    }
    public static void RunMemory(string output){
        Directory.CreateDirectory(output);
        string input=Path.Combine(output,"test-scene.png");
        using(var fixture=new Bitmap(4096,2730)){
            using(var g=Graphics.FromImage(fixture)){g.Clear(Color.BurlyWood);g.FillEllipse(Brushes.SeaGreen,300,200,2600,1800);g.FillRectangle(Brushes.SlateBlue,2900,1000,900,1400);}
            fixture.Save(input);
        }
        using var n=new NativeEngine(Path.Combine(AppContext.BaseDirectory,"resources"));
        const string settings="{\"film_stock\":\"kodak_portra_400\",\"print_stock\":\"kodak_portra_endura\",\"scan_film\":false,\"grain_active\":true,\"halation_active\":true,\"glare_active\":false,\"print_exposure\":1}";
        var results=new List<object>();
        for(int i=0;i<3;i++){
            bool limited=PhotoImport.Open(n,input,settings);
            var rendered=n.Pixels();
            string png=Path.Combine(output,$"rendered-{i}.png");
            ImageFiles.SaveAtomic(rendered,n.Width,n.Height,png,"png");
            using var decoded=new Bitmap(png);
            if(!ImageFiles.Pixels(decoded).SequenceEqual(rendered))throw new Exception("Memory-mode export differs from preview");
            int width=n.Width,height=n.Height;
            try{PhotoImport.Open(n,input+".missing",settings);throw new Exception("Missing input accepted");}
            catch(Exception e) when(e is not OutOfMemoryException && e.Message!="Missing input accepted"){
                if(n.Width!=width||n.Height!=height||!n.Pixels().SequenceEqual(rendered))throw new Exception("Failed import destroyed previous photo");
            }
            results.Add(new{width,height,limited,png_pixel_agreement=true});
        }
        File.WriteAllText(Path.Combine(output,"memory-result.json"),JsonSerializer.Serialize(new{passed=true,source_width=4096,source_height=2730,results}));
    }
}
