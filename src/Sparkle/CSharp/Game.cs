using Bliss.CSharp;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Batches.Primitives;
using Bliss.CSharp.Graphics.Rendering.Renderers.Batches.Sprites;
using Bliss.CSharp.Images;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Contexts;
using Bliss.CSharp.Logging;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using Bliss.CSharp.Windowing;
using MiniAudioEx.Core.StandardAPI;
using Sparkle.CSharp.Content;
using Sparkle.CSharp.Graphics;
using Sparkle.CSharp.GUI;
using Sparkle.CSharp.Logging;
using Sparkle.CSharp.Overlays;
using Sparkle.CSharp.Registries;
using Sparkle.CSharp.Scenes;
using Veldrid;
using JLogger = Jitter2.Logger;

namespace Sparkle.CSharp;

public class Game : Disposable {
    
    /// <summary>
    /// The version of the game engine (Sparkle).
    /// </summary>
    public static readonly Version Version = new Version(5, 0, 0);

    /// <summary>
    /// The singleton instance of the game.
    /// </summary>
    public static Game? Instance { get; private set; }
    
    /// <summary>
    /// The settings for the game.
    /// </summary>
    public GameSettings Settings { get; private set; }
    
    /// <summary>
    /// The main window of the game.
    /// </summary>
    public IWindow MainWindow { get; private set; }
    
    /// <summary>
    /// The graphics device for rendering.
    /// </summary>
    public GraphicsDevice GraphicsDevice { get; private set; }
    
    /// <summary>
    /// The command list used for rendering commands.
    /// </summary>
    public CommandList CommandList { get; private set; }

    /// <summary>
    /// Represents the fullscreen render pass used during the rendering process.
    /// </summary>
    public FullScreenRenderer FullScreenRenderPass { get; private set; }

    /// <summary>
    /// The default (global) sprite batch used for rendering 2D sprites.
    /// </summary>
    public SpriteBatch GlobalSpriteBatch { get; private set; }

    /// <summary>
    /// The default (global) primitive batch used for rendering 2D geometric primitives.
    /// </summary>
    public PrimitiveBatch GlobalPrimitiveBatch { get; private set; }

    /// <summary>
    /// The default (global) immediate renderer used for rendering (Vertices...) in immediate mode.
    /// </summary>
    public ImmediateRenderer GlobalImmediateRenderer { get; private set; }

    /// <summary>
    /// An instance encapsulating core graphics rendering components associated with the game.
    /// </summary>
    public GraphicsContext GraphicsContext { get; private set; }
    
    /// <summary>
    /// The content manager used to load game assets.
    /// </summary>
    public ContentManager Content { get; private set; }
    
    /// <summary>
    /// Flag to indicate if the game should close.
    /// </summary>
    public bool ShouldClose;
    
    /// <summary>
    /// The render target.
    /// </summary>
    private RenderTexture2D _renderTarget;
    
    /// <summary>
    /// The render result.
    /// </summary>
    private Texture2D _renderResult;
    
    /// <summary>
    /// The log file writer used for logging messages to a file.
    /// </summary>
    private LogFileWriter _logFileWriter;
    
    /// <summary>
    /// The logger for jitter.
    /// </summary>
    private LogJitter _logJitter;
    
    /// <summary>
    /// The fixed frame rate for the game.
    /// </summary>
    private double _fixedFrameRate;
    
    /// <summary>
    /// The time step for fixed updates.
    /// </summary>
    private readonly double _fixedUpdateTimeStep;
    
    /// <summary>
    /// The timer for tracking the fixed update time.
    /// </summary>
    private double _fixedUpdateTimer;
    
    /// <summary>
    /// Initializes the <see cref="Game"/> with specified settings.
    /// </summary>
    /// <param name="settings">The game settings.</param>
    public Game(GameSettings settings) {
        Instance = this;
        Settings = settings;
        _fixedUpdateTimeStep = settings.FixedTimeStep;
    }
    
    /// <summary>
    /// Starts the game loop, initializing all necessary components and running the game.
    /// </summary>
    /// <param name="scene">The scene to load initially.</param>
    public void Run(Scene? scene) {
        if (Settings.LogDirectory != string.Empty) {
            _logFileWriter = new LogFileWriter(Settings.LogDirectory);
            Logger.Message += _logFileWriter.WriteFileMsg;
        }
        
        // Setup jitter logger.
        _logJitter = new LogJitter();
        JLogger.Listener += _logJitter.Log;
        
        Logger.Info($"Sparkle [{Version}] start...");
        Logger.Info($"\t> CPU: {SystemInfo.Cpu}");
        Logger.Info($"\t> MEMORY: Total: {SystemInfo.MemoryInfo.Total} MB, Available: {SystemInfo.MemoryInfo.Available} MB");
        Logger.Info($"\t> THREADS: {SystemInfo.Threads}");
        Logger.Info($"\t> OS: {SystemInfo.Os}");
        
        Logger.Info("Initialize window and graphics device...");
        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            Debug = false,
            HasMainSwapchain = true,
            SwapchainDepthFormat = PixelFormat.D32FloatS8UInt,
            SyncToVerticalBlank = Settings.VSync,
            ResourceBindingModel = ResourceBindingModel.Improved,
            PreferDepthRangeZeroToOne = true,
            PreferStandardClipSpaceYDirection = true,
            SwapchainSrgbFormat = false
        };
        
        MainWindow = Window.CreateWindow(WindowType.Sdl3, Settings.Size.Width, Settings.Size.Height, Settings.Title, Settings.WindowFlags, options, Settings.Backend, out GraphicsDevice graphicsDevice);
        MainWindow.SetMinimumSize(Settings.MinSize.Width, Settings.MinSize.Height);
        MainWindow.Resized += () => OnResize(new Rectangle(MainWindow.GetX(), MainWindow.GetY(), MainWindow.GetWidth(), MainWindow.GetHeight()));
        GraphicsDevice = graphicsDevice;
        
        Logger.Info("\t> Window Info:");
        Logger.Info($"\t \t> Window type: {WindowType.Sdl3}");
        Logger.Info($"\t \t> Window Size: {MainWindow.GetWidth()} x {MainWindow.GetHeight()}");
        Logger.Info("\t> Device Info:");
        Logger.Info($"\t \t> Vendor: {GraphicsDevice.VendorName}");
        Logger.Info($"\t \t> Renderer: {GraphicsDevice.DeviceName}");
        Logger.Info($"\t \t> Backend type: {GraphicsDevice.BackendType}, Version: {GraphicsDevice.ApiVersion}");
        
        Logger.Info("Loading window icon...");
        MainWindow.SetIcon(Settings.IconPath != string.Empty ? new Image(Settings.IconPath) : new Image("content/sparkle/images/icon.png"));
        
        Logger.Info("Initialize input...");
        if (MainWindow is Sdl3Window) {
            Input.Init(new Sdl3InputContext(MainWindow));
        }
        else {
            Logger.Fatal("This type of window is not supported by the InputContext!");
        }
        
        Logger.Info("Initialize command list...");
        CommandList = graphicsDevice.ResourceFactory.CreateCommandList();
        
        Logger.Info("Initialize time...");
        Time.Init();
        
        Logger.Info($"Set target FPS to: {Settings.TargetFps}");
        SetTargetFps(Settings.TargetFps);
        
        Logger.Info("Initialize audio device...");
        AudioContext.Initialize(44100, 2);
        
        Logger.Info("Initialize global resources...");
        GlobalResource.Init(graphicsDevice);
        
        Logger.Info("Initialize global graphics assets...");
        GlobalGraphicsAssets.Init(graphicsDevice, MainWindow);
        
        Logger.Info("Initialize full screen renderer...");
        FullScreenRenderPass = new FullScreenRenderer(graphicsDevice);
        
        Logger.Info("Initialize render target texture...");
        _renderTarget = new RenderTexture2D(graphicsDevice, (uint) MainWindow.GetWidth(), (uint) MainWindow.GetHeight(), sampleCount: Settings.SampleCount);
        _renderResult = new Texture2D(graphicsDevice, new Image(MainWindow.GetWidth(), MainWindow.GetHeight()), false);
        
        Logger.Info("Initialize global sprite batch...");
        GlobalSpriteBatch = new SpriteBatch(graphicsDevice, MainWindow);
        
        Logger.Info("Initialize global primitive batch...");
        GlobalPrimitiveBatch = new PrimitiveBatch(graphicsDevice, MainWindow);
        
        Logger.Info("Initialize global immediate renderer...");
        GlobalImmediateRenderer = new ImmediateRenderer(graphicsDevice);
        
        Logger.Info("Initialize graphics context...");
        GraphicsContext = new GraphicsContext(graphicsDevice, CommandList, FullScreenRenderPass, GlobalSpriteBatch, GlobalPrimitiveBatch, GlobalImmediateRenderer);
        
        Logger.Info("Initialize content manager...");
        Content = new ContentManager(graphicsDevice);
        
        Logger.Info("Initialize overlay manager...");
        OverlayManager.Init();
        
        Logger.Info("Initialize GUI manager...");
        GuiManager.Init();
        
        Logger.Info("Initialize registry manager...");
        RegistryManager.Init();
        
        Logger.Info("Initialize scene manager...");
        SceneManager.Init(graphicsDevice, MainWindow, scene, Settings.SampleCount);
        
        OnRun();
        
        Logger.Info("Load content...");
        Load(Content);
        
        Init();
        
        Logger.Info("Start game loop...");
        while (!ShouldClose && MainWindow.Exists) {
            if (GetTargetFps() != 0 && Time.DeltaTimer.Elapsed.TotalSeconds <= _fixedFrameRate) {
                continue;
            }
            Time.Update();
            
            MainWindow.PumpEvents();
            Input.Begin();
            
            AudioContext.Update();
            Update(Time.Delta);
            AfterUpdate(Time.Delta);

            _fixedUpdateTimer += Time.Delta;
            while (_fixedUpdateTimer >= _fixedUpdateTimeStep) {
                FixedUpdate(_fixedUpdateTimeStep);
                _fixedUpdateTimer -= _fixedUpdateTimeStep;
            }
            
            // Draw.
            CommandList.Begin();
            CommandList.SetFramebuffer(_renderTarget.Framebuffer);
            CommandList.ClearColorTarget(0, Color.DarkGray.ToRgbaFloat());
            CommandList.ClearDepthStencil(1.0F);
            
            Draw(GraphicsContext, _renderTarget.Framebuffer);
            
            // Apply MSAA.
            if (_renderTarget.SampleCount != TextureSampleCount.Count1) {
                CommandList.ResolveTexture(_renderTarget.ColorTexture, _renderResult.DeviceTexture);
            }
            else {
                CommandList.CopyTexture(_renderTarget.ColorTexture, _renderResult.DeviceTexture);
            }
            
            // Draw render target texture.
            CommandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
            CommandList.ClearColorTarget(0, Color.DarkGray.ToRgbaFloat());
            
            FullScreenRenderPass.Draw(CommandList, _renderResult, graphicsDevice.SwapchainFramebuffer.OutputDescription);
            
            CommandList.End();
            graphicsDevice.WaitForIdle();
            graphicsDevice.SubmitCommands(CommandList);
            graphicsDevice.SwapBuffers();
            
            Input.End();
        }
        
        Logger.Warn("Application shuts down!");
        OnClose();
    }

    /// <summary>
    /// Virtual method for additional setup when the game starts.
    /// </summary>
    protected virtual void OnRun() { }

    /// <summary>
    /// Loads the required game content and resources.
    /// </summary>
    protected virtual void Load(ContentManager content) {
        RegistryManager.OnLoad(content);
    }
    
    /// <summary>
    /// Initializes global game resources.
    /// </summary>
    protected virtual void Init() {
        RegistryManager.OnInit();
        SceneManager.OnInit();
    }

    /// <summary>
    /// Updates the game state, including scene and UI management.
    /// </summary>
    /// <param name="delta">The time delta since the last update.</param>
    protected virtual void Update(double delta) {
        SceneManager.OnUpdate(delta);
        OverlayManager.OnUpdate(delta);
        GuiManager.OnUpdate(delta);
    }
    
    /// <summary>
    /// Final update after regular updates are completed.
    /// </summary>
    /// <param name="delta">The time delta since the last update.</param>
    protected virtual void AfterUpdate(double delta) {
        SceneManager.OnAfterUpdate(delta);
        OverlayManager.OnAfterUpdate(delta);
        GuiManager.OnAfterUpdate(delta);
    }
    
    /// <summary>
    /// Executes fixed update logic with the specified time step.
    /// </summary>
    /// <param name="fixedStep">The fixed time step in seconds.</param>
    protected virtual void FixedUpdate(double fixedStep) {
        SceneManager.OnFixedUpdate(fixedStep);
        OverlayManager.OnFixedUpdate(fixedStep);
        GuiManager.OnFixedUpdate(fixedStep);
    }
    
    /// <summary>
    /// Renders the game scene to the screen.
    /// </summary>
    protected virtual void Draw(GraphicsContext context, Framebuffer framebuffer) {
        SceneManager.OnDraw(context, framebuffer);
        OverlayManager.OnDraw(context, framebuffer);
        GuiManager.OnDraw(context, framebuffer);
    }
    
    /// <summary>
    /// Handles window resizing events.
    /// </summary>
    protected virtual void OnResize(Rectangle rectangle) {
        
        // Resize main swapchain.
        GraphicsDevice.MainSwapchain.Resize((uint) rectangle.Width, (uint) rectangle.Height);
        
        // Resize render target.
        _renderTarget.Resize((uint) rectangle.Width, (uint) rectangle.Height);
        _renderResult.Dispose();
        _renderResult = new Texture2D(GraphicsDevice, new Image(rectangle.Width, rectangle.Height), false);
        
        // Resize scene manager.
        SceneManager.OnResize(rectangle);

        // Resize overlay manager.
        OverlayManager.OnResize(rectangle);
        
        // Resize gui manager.
        GuiManager.OnResize(rectangle);
    }
    
    /// <summary>
    /// Handles the logic to be executed when the application shuts down.
    /// This method can be overridden by derived classes to include custom shutdown behavior.
    /// </summary>
    protected virtual void OnClose() { }
    
    /// <summary>
    /// Gets the target frames per second.
    /// </summary>
    public int GetTargetFps() {
        return (int) (1.0F / _fixedFrameRate);
    }
    
    /// <summary>
    /// Sets the target frames per second.
    /// </summary>
    public void SetTargetFps(int fps) {
        _fixedFrameRate = 1.0F / fps;
    }
    
    /// <summary>
    /// Retrieves the texture sample count currently used by the game's MSAA render target texture.
    /// </summary>
    /// <returns>The sample count of the MSAA render target texture.</returns>
    public TextureSampleCount? GetSampleCount() {
        return _renderTarget.SampleCount;
    }
    
    /// <summary>
    /// Sets the sample count for multi-sample anti-aliasing (MSAA).
    /// </summary>
    /// <param name="sampleCount">The texture sample count to apply, defining the level of anti-aliasing.</param>
    public void SetSampleCount(TextureSampleCount sampleCount) {
        _renderTarget.SampleCount = sampleCount;
        SceneManager.FilterTarget.SampleCount = sampleCount;
    }
    
    protected override void Dispose(bool disposing) {
        if (disposing) {
            SceneManager.Destroy();
            OverlayManager.Destroy();
            GuiManager.Destroy();
            RegistryManager.Destroy();
            
            Content.Dispose();
            
            _renderTarget.Dispose();
            _renderResult.Dispose();
            FullScreenRenderPass.Dispose();
            
            GlobalImmediateRenderer.Dispose();
            GlobalPrimitiveBatch.Dispose();
            GlobalSpriteBatch.Dispose();
            
            CommandList.Dispose();
            
            GlobalGraphicsAssets.Destroy();
            GlobalResource.Destroy();
            
            AudioContext.Deinitialize();
            
            GraphicsDevice.Dispose();
            MainWindow.Dispose();

            JLogger.Listener -= _logJitter.Log;
            Logger.Message -= _logFileWriter.WriteFileMsg;
        }
    }
}