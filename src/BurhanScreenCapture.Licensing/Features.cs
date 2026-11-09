namespace BurhanScreenCapture.Licensing;

public enum FeatureTier { Free, Pro }

/// <summary>Every user-facing capability with an explicit tier. Enforcement goes through <see cref="FeatureCatalog.IsAllowed"/>.</summary>
public enum Feature
{
    // ---- Free: capture
    FullScreenCapture, RegionCapture, WindowCapture, MultiMonitorCapture, DelayedCapture,
    CopyToClipboard, PngJpgExport, AutoSave, CapturePreview, GlobalHotkeys,
    // ---- Free: recording
    ScreenRecording, RegionRecording, WindowRecording, PauseResume, SystemAudio, Microphone, Mp4Export,
    // ---- Free: editor
    Crop, Resize, Rotate, TextAnnotation, Arrow, Line, Rectangle, Ellipse, BasicColors, UndoRedo,
    // ---- Free: history (latest 20)
    History,

    // ---- Pro
    ScrollingCapture, AdvancedCaptureModes, ScreenshotTemplates,
    Blur, Pixelate, Magnifier, Spotlight, AdvancedAnnotation, Watermark, AdvancedCompression, BatchProcessing,
    GifRecording, RecordingProfiles, AdvancedVideoExport,
    Ocr, HistorySearch, UnlimitedHistory, AdvancedExport
}

public sealed record FeatureInfo(Feature Feature, FeatureTier Tier, string Name, string Description, string[] Benefits);

public static class FeatureCatalog
{
    public const int FreeHistoryLimit = 20;

    private static readonly Dictionary<Feature, FeatureInfo> Map = Build();

    public static FeatureInfo Get(Feature f) => Map[f];
    public static IReadOnlyCollection<FeatureInfo> All => Map.Values;
    public static FeatureTier TierOf(Feature f) => Map[f].Tier;

    public static bool IsAllowed(Feature f, Edition edition) =>
        TierOf(f) == FeatureTier.Free || edition is Edition.Pro or Edition.ProTrial;

    private static Dictionary<Feature, FeatureInfo> Build()
    {
        var d = new Dictionary<Feature, FeatureInfo>();
        void Free(Feature f, string name, string desc) => d[f] = new(f, FeatureTier.Free, name, desc, Array.Empty<string>());
        void Pro(Feature f, string name, string desc, params string[] benefits) => d[f] = new(f, FeatureTier.Pro, name, desc, benefits);

        Free(Feature.FullScreenCapture, "Full Screen Capture", "Capture the entire screen.");
        Free(Feature.RegionCapture, "Selected Area Capture", "Drag to capture any part of the screen.");
        Free(Feature.WindowCapture, "Active Window Capture", "Capture the window you are working in.");
        Free(Feature.MultiMonitorCapture, "Multi-Monitor Capture", "Capture all monitors at once.");
        Free(Feature.DelayedCapture, "Delayed Capture", "Capture after 3, 5 or 10 seconds.");
        Free(Feature.CopyToClipboard, "Copy to Clipboard", "Copy captures straight to the clipboard.");
        Free(Feature.PngJpgExport, "PNG & JPG Export", "Save as PNG or JPG.");
        Free(Feature.AutoSave, "Automatic Saving", "Save every capture automatically.");
        Free(Feature.CapturePreview, "Screenshot Preview", "Preview a capture before saving or editing.");
        Free(Feature.GlobalHotkeys, "Keyboard Shortcuts", "Capture from anywhere with global shortcuts.");
        Free(Feature.ScreenRecording, "Screen Recording", "Record the full screen to MP4.");
        Free(Feature.RegionRecording, "Area Recording", "Record a selected screen area.");
        Free(Feature.WindowRecording, "Window Recording", "Record a single window.");
        Free(Feature.PauseResume, "Pause & Resume", "Pause and resume recordings.");
        Free(Feature.SystemAudio, "System Audio", "Record computer sound.");
        Free(Feature.Microphone, "Microphone", "Record your voice.");
        Free(Feature.Mp4Export, "MP4 Export", "Recordings are saved as MP4.");
        Free(Feature.Crop, "Crop", "Crop images.");
        Free(Feature.Resize, "Resize", "Resize images.");
        Free(Feature.Rotate, "Rotate", "Rotate images.");
        Free(Feature.TextAnnotation, "Text", "Add text.");
        Free(Feature.Arrow, "Arrow", "Draw arrows.");
        Free(Feature.Line, "Line", "Draw lines.");
        Free(Feature.Rectangle, "Rectangle", "Draw rectangles.");
        Free(Feature.Ellipse, "Circle", "Draw circles and ellipses.");
        Free(Feature.BasicColors, "Colors", "Choose annotation colors.");
        Free(Feature.UndoRedo, "Undo & Redo", "Undo and redo edits.");
        Free(Feature.History, "Screenshot History", "Your latest 20 screenshots.");

        Pro(Feature.ScrollingCapture, "Scrolling Screenshot", "Automatically scroll and capture long pages, chats and documents in one image.",
            "Capture an entire web page or document", "Automatic scrolling and stitching", "No manual joining of screenshots");
        Pro(Feature.AdvancedCaptureModes, "Advanced Capture Modes", "Repeat last area and fixed-size capture for consistent screenshots.",
            "Capture the same area again with one click", "Exact pixel sizes for tutorials and stores");
        Pro(Feature.ScreenshotTemplates, "Screenshot Templates", "Present screenshots on beautiful backgrounds with padding, rounded corners and shadow.",
            "Social-media ready images", "Consistent, branded look");
        Pro(Feature.Blur, "Blur", "Blur sensitive areas such as passwords and personal data.",
            "Protect private information", "Share screenshots safely");
        Pro(Feature.Pixelate, "Pixelate", "Pixelate any area so it cannot be read.",
            "Hide names, numbers and faces", "Irreversible once saved");
        Pro(Feature.Magnifier, "Magnifier", "Add a zoomed-in lens to highlight small details.",
            "Make small text readable", "Great for tutorials");
        Pro(Feature.Spotlight, "Spotlight", "Dim everything except the area you want people to look at.",
            "Direct attention instantly", "Professional documentation");
        Pro(Feature.AdvancedAnnotation, "Advanced Annotation", "Highlighter, numbered steps and filled shapes.",
            "Step-by-step guides", "Clear highlights");
        Pro(Feature.Watermark, "Custom Watermark", "Stamp your name or brand on images.",
            "Protect your work", "Promote your brand");
        Pro(Feature.AdvancedCompression, "Advanced Compression", "Control JPG quality to get smaller files.",
            "Smaller uploads", "Quality you choose");
        Pro(Feature.BatchProcessing, "Batch Processing", "Resize, convert, compress and watermark many images at once.",
            "Save hours of repetitive work");
        Pro(Feature.GifRecording, "GIF Recording", "Record short animated GIFs of your screen.",
            "Perfect for chats, docs and bug reports");
        Pro(Feature.RecordingProfiles, "Recording Profiles", "One-click presets such as Tutorial, YouTube HD and Small File.",
            "The right settings every time");
        Pro(Feature.AdvancedVideoExport, "Advanced Video Export", "H.265/HEVC encoding and custom bitrate.",
            "Smaller files at the same quality");
        Pro(Feature.Ocr, "OCR — Extract Text", "Copy text from any screenshot or image (offline, uses Windows OCR).",
            "No retyping", "Works offline");
        Pro(Feature.HistorySearch, "History Search", "Search your screenshot history by name, date or text.",
            "Find any capture instantly");
        Pro(Feature.UnlimitedHistory, "Unlimited History", "Keep every screenshot in history (subject to disk space).",
            "Never lose track of a capture");
        Pro(Feature.AdvancedExport, "Advanced Export", "Export to BMP, TIFF and GIF and choose output size.",
            "Compatible with any workflow");
        return d;
    }
}
