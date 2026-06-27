using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public static class Game
    {
        // Initialize globals here.
        public static Camera3D CurrentCamera = new(new Vector3(-25,25,-25),new Vector3(0,0,0), Vector3.UnitY,75f,CameraProjection.Perspective);
        public static Workspace Workspace = new();

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

        private static void DrawGame3D()
        {
            Workspace.Draw3DWorkspace();
            //Raylib.DrawSphere(new Vector3(0,2.5f,0),5f,Color.Orange);
            //Raylib.DrawPlane(Vector3.Zero,new Vector2(64,64),Color.Green);
        }


        public static void Setup()
        {
            Raylib.InitWindow(1280, 720, "Raylib 3D | PhrawgEngine");
            Raylib.SetTargetFPS(60);
        }

        public static void Run()
        {
            LoadGame();
            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.DarkGray);
                UpdateGame(dt);
                Raylib.BeginMode3D(CurrentCamera);
                    DrawGame3D();
                Raylib.EndMode3D();
                    DrawGame2D();
                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();
        }
    } 
}
