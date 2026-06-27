using PhrawgEngine;
using Raylib_cs;

namespace Raylib3D
{
    class Program
    {
        static Font font;
        static Font font_bold;
        static Font font_mono;
        static Font font_mono_bold;

        static void Main(string[] args)
        {
            try
            {
                Game.Setup();
                LoadFonts();
                Game.Run();
            }
            catch (Exception ex)
            {
                // The crash may have happened before LoadFonts() ran. If a GL
                // context exists try to load them now so the error screen has
                // proper fonts; CrashHandler also falls back to the default font.
                if (font_mono.Texture.Id == 0 && Raylib.IsWindowReady())
                    LoadFonts();

                CrashHandler.ShowErrorScreen(ex, font, font_bold, font_mono, font_mono_bold);
            }
            finally
            {
                if (Raylib.IsWindowReady())
                {
                    UnloadFonts();
                    Raylib.CloseWindow();
                }
            }
        }

        static void LoadFonts()
        {
            font           = Raylib.LoadFontEx("src/engine/builtin/fonts/Ubuntu/Ubuntu-Regular.ttf", 32, null, 0);
            font_bold      = Raylib.LoadFontEx("src/engine/builtin/fonts/Ubuntu/Ubuntu-Bold.ttf", 32, null, 0);
            font_mono      = Raylib.LoadFontEx("src/engine/builtin/fonts/Ubuntu_Mono/UbuntuMono-Regular.ttf", 32, null, 0);
            font_mono_bold = Raylib.LoadFontEx("src/engine/builtin/fonts/Ubuntu_Mono/UbuntuMono-Bold.ttf", 32, null, 0);

            // Smooth scaling for all four.
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(font_bold.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(font_mono.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(font_mono_bold.Texture, TextureFilter.Bilinear);
        }

        static void UnloadFonts()
        {
            Raylib.UnloadFont(font);
            Raylib.UnloadFont(font_bold);
            Raylib.UnloadFont(font_mono);
            Raylib.UnloadFont(font_mono_bold);
        }
    }
}