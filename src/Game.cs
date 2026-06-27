using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public static class Game
    {
        // Initialize globals here.
        public static Camera3D CurrentCamera = new(new Vector3(-25,25,-25),new Vector3(0,0,0), Vector3.UnitY,75f,CameraProjection.Perspective);
        public static Workspace Workspace = new();

        // The HDR render pipeline. Renderer components register with this.
        public static RenderPipeline Pipeline = new();

        private static void LoadGame()
        {
            Console.WriteLine("Game started.");
            SceneLoader.LoadSceneFromFile("src/engine/builtin/scenes/demo.scene");
        }

        private static void UpdateGame(float dt)
        {
            Workspace.UpdateWorkspace(dt);

            if (Raylib.IsKeyDown(KeyboardKey.Enter))
            {
                throw new InvalidOperationException("Something went wrong!");
            }
        }

        private static void DrawGame2D()
        {
            Raylib.DrawText("Raylib3D | PhrawgEngine", 10, 10, 20, Color.White);
            Workspace.Draw2DWorkspace();
        }

        public static void Setup()
        {
            Raylib.InitWindow(1280, 720, "Raylib 3D | PhrawgEngine");
            Raylib.SetTargetFPS(60);

            // Pipeline needs a GL context, so init after the window exists.
            Pipeline.Init(1280, 720);
        }

        public static void Run()
        {
            LoadGame();
            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                // Update first so the camera (FreeCam) is current before we render.
                UpdateGame(dt);

                Raylib.BeginDrawing();
                    Raylib.ClearBackground(Color.DarkGray);

                    // HDR scene pass + tonemap composite to the backbuffer.
                    Pipeline.RenderFrame(CurrentCamera);

                    // Legacy immediate-mode renderers (SimpleSphere/Plane) still
                    // draw via the component callback, unshaded, over the composite.
                    Raylib.BeginMode3D(CurrentCamera);
                        Workspace.Draw3DWorkspace();
                    Raylib.EndMode3D();

                    DrawGame2D();
                Raylib.EndDrawing();
            }

            Pipeline.Shutdown();
            Raylib.CloseWindow();
        }
    }
}