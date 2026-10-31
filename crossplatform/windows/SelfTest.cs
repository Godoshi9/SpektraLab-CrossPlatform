using System.Text.Json;
namespace SpektraLab;
internal static class SelfTest {
    public static void Run(string output){
        Directory.CreateDirectory(output);
        using var n=new NativeEngine(Path.Combine(AppContext.BaseDirectory,"resources"));
        using var fixture=new Bitmap(640,420);
        using(var g=Graphics.FromImage(fixture)){g.Clear(Color.FromArgb(175,181,157));g.FillRectangle(Brushes.DarkOliveGreen,0,210,640,210);g.FillEllipse(Brushes.Peru,190,90,230,260);g.FillRectangle(Brushes.Bisque,245,65,100,65);g.FillRectangle(Brushes.IndianRed,60,250,100,120);g.FillRectangle(Brushes.SlateBlue,470,160,105,220);}
        var input=Path.Combine(output,"test-scene.png");fixture.Save(input);
        using var decoded=ImageFiles.Decode(input,4096);
        const string settings="{\"film_stock\":\"kodak_portra_400\",\"print_stock\":\"kodak_portra_endura\",\"scan_film\":false,\"grain_active\":false,\"halation_active\":true,\"glare_active\":false,\"print_exposure\":1}";
        n.OpenPixels(ImageFiles.Pixels(decoded),decoded.Width,decoded.Height,settings);
        var rendered=n.Pixels();var png=Path.Combine(output,"rendered.png");
        ImageFiles.SaveAtomic(rendered,n.Width,n.Height,png,"png");
        using var read=new Bitmap(png);
        if(!ImageFiles.Pixels(read).SequenceEqual(rendered))throw new Exception("PNG export differs from native preview pixels");
        var jpg=Path.Combine(output,"rendered.jpg");ImageFiles.SaveAtomic(rendered,n.Width,n.Height,jpg,"jpg");
        using var readJpg=new Bitmap(jpg);if(readJpg.Width!=n.Width||readJpg.Height!=n.Height)throw new Exception("JPEG dimensions differ");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,backend="native-vulkan",width=n.Width,height=n.Height,elapsed_ms=n.Elapsed,png_pixel_agreement=true,jpeg_decoded=true}));
    }
}
