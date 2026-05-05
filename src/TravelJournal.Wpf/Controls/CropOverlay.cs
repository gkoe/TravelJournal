using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TravelJournal.Wpf.Controls;

public sealed class CropOverlay : FrameworkElement
{
    // ── Static brushes / pens ─────────────────────────────────

    private static readonly Brush OverlayBrush;
    private static readonly Pen   SelectionPen;
    private static readonly Brush HandleBrush = Brushes.White;
    private static readonly Pen   HandlePen;

    static CropOverlay()
    {
        OverlayBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        OverlayBrush.Freeze();
        SelectionPen = new Pen(Brushes.White, 1.5)
        {
            DashStyle = new DashStyle(new[] { 5.0, 3.0 }, 0)
        };
        SelectionPen.Freeze();
        HandlePen = new Pen(Brushes.Black, 0.8);
        HandlePen.Freeze();
    }

    // ── Dependency Properties ─────────────────────────────────

    public static readonly DependencyProperty ImagePixelWidthProperty =
        DependencyProperty.Register(nameof(ImagePixelWidth), typeof(int?), typeof(CropOverlay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImagePixelHeightProperty =
        DependencyProperty.Register(nameof(ImagePixelHeight), typeof(int?), typeof(CropOverlay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedCropRectProperty =
        DependencyProperty.Register(nameof(SelectedCropRect), typeof(Int32Rect), typeof(CropOverlay),
            new FrameworkPropertyMetadata(
                Int32Rect.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedCropRectChanged));

    public static readonly DependencyProperty ConfirmCommandProperty =
        DependencyProperty.Register(nameof(ConfirmCommand), typeof(ICommand), typeof(CropOverlay));

    public int?      ImagePixelWidth  { get => (int?)GetValue(ImagePixelWidthProperty);  set => SetValue(ImagePixelWidthProperty, value); }
    public int?      ImagePixelHeight { get => (int?)GetValue(ImagePixelHeightProperty); set => SetValue(ImagePixelHeightProperty, value); }
    public Int32Rect SelectedCropRect { get => (Int32Rect)GetValue(SelectedCropRectProperty); set => SetValue(SelectedCropRectProperty, value); }
    public ICommand? ConfirmCommand   { get => (ICommand?)GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }

    private static void OnSelectedCropRectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CropOverlay o && ((Int32Rect)e.NewValue).IsEmpty)
        {
            o._hasSelection = false;
            o.InvalidateVisual();
        }
    }

    // ── Private state ─────────────────────────────────────────

    private Point _dragStart;
    private Point _dragEnd;
    private bool  _isDragging;
    private bool  _hasSelection;

    public CropOverlay()
    {
        Cursor          = Cursors.Cross;
        Focusable       = true;
        FocusVisualStyle = null;
    }

    // ── Mouse ─────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!HasImageDimensions) return;
        _dragStart    = Clamp(e.GetPosition(this));
        _dragEnd      = _dragStart;
        _isDragging   = true;
        _hasSelection = false;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;
        _dragEnd = Clamp(e.GetPosition(this));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _dragEnd    = Clamp(e.GetPosition(this));
        _isDragging = false;
        ReleaseMouseCapture();

        var screen = NormalizedRect(_dragStart, _dragEnd);
        if (screen.Width >= 4 && screen.Height >= 4)
        {
            _hasSelection = true;
            SelectedCropRect = ToImageRect(screen);
            Keyboard.Focus(this);
        }
        else
        {
            _hasSelection = false;
            SelectedCropRect = Int32Rect.Empty;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    // ── Keyboard ──────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Return && _hasSelection)
        {
            if (ConfirmCommand?.CanExecute(null) == true)
                ConfirmCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _hasSelection)
        {
            _hasSelection    = false;
            SelectedCropRect = Int32Rect.Empty;
            InvalidateVisual();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    // ── Rendering ─────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Transparentes Rechteck über den gesamten Bereich — stellt Hit-Testing sicher.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (!HasImageDimensions || ActualWidth < 1 || ActualHeight < 1) return;

        var (ox, oy, iw, ih) = GetImageBounds();
        var imgRect = new Rect(ox, oy, iw, ih);

        if (!_hasSelection && !_isDragging) return;

        var sel = _isDragging
            ? Intersect(NormalizedRect(_dragStart, _dragEnd), imgRect)
            : ScreenRectOf(SelectedCropRect);

        if (sel.IsEmpty || sel.Width < 1 || sel.Height < 1) return;

        // 4 dark strips around selection
        FillStrip(dc, ox,         oy,          iw,             sel.Top - oy);
        FillStrip(dc, ox,         sel.Bottom,  iw,             oy + ih - sel.Bottom);
        FillStrip(dc, ox,         sel.Top,     sel.Left - ox,  sel.Height);
        FillStrip(dc, sel.Right,  sel.Top,     ox + iw - sel.Right, sel.Height);

        // Dashed border
        dc.DrawRectangle(null, SelectionPen, sel);

        // Corner handles
        const double hs = 7;
        DrawHandle(dc, sel.Left  - hs / 2, sel.Top    - hs / 2, hs);
        DrawHandle(dc, sel.Right - hs / 2, sel.Top    - hs / 2, hs);
        DrawHandle(dc, sel.Left  - hs / 2, sel.Bottom - hs / 2, hs);
        DrawHandle(dc, sel.Right - hs / 2, sel.Bottom - hs / 2, hs);
    }

    private static void FillStrip(DrawingContext dc, double x, double y, double w, double h)
    {
        if (w > 0 && h > 0) dc.DrawRectangle(OverlayBrush, null, new Rect(x, y, w, h));
    }

    private static void DrawHandle(DrawingContext dc, double x, double y, double size)
        => dc.DrawRectangle(HandleBrush, HandlePen, new Rect(x, y, size, size));

    // ── Coordinate helpers ────────────────────────────────────

    private bool HasImageDimensions => ImagePixelWidth is > 0 && ImagePixelHeight is > 0;

    private (double ox, double oy, double iw, double ih) GetImageBounds()
    {
        var pw = (double)ImagePixelWidth!.Value;
        var ph = (double)ImagePixelHeight!.Value;
        var imgAspect  = pw / ph;
        var ctrlAspect = ActualWidth / ActualHeight;

        if (imgAspect >= ctrlAspect)
        {
            var h = ActualWidth / imgAspect;
            return (0, (ActualHeight - h) / 2, ActualWidth, h);
        }
        else
        {
            var w = ActualHeight * imgAspect;
            return ((ActualWidth - w) / 2, 0, w, ActualHeight);
        }
    }

    private Point Clamp(Point p)
    {
        var (ox, oy, iw, ih) = GetImageBounds();
        return new Point(
            Math.Clamp(p.X, ox, ox + iw),
            Math.Clamp(p.Y, oy, oy + ih));
    }

    private static Rect NormalizedRect(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
               Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private static Rect Intersect(Rect a, Rect b)
    {
        var r = Rect.Intersect(a, b);
        return r.IsEmpty ? new Rect(0, 0, 0, 0) : r;
    }

    private Int32Rect ToImageRect(Rect screen)
    {
        var (ox, oy, iw, ih) = GetImageBounds();
        var pw = ImagePixelWidth!.Value;
        var ph = ImagePixelHeight!.Value;

        var ix = (int)Math.Round((screen.X - ox) / iw * pw);
        var iy = (int)Math.Round((screen.Y - oy) / ih * ph);
        var iW = (int)Math.Round(screen.Width  / iw * pw);
        var iH = (int)Math.Round(screen.Height / ih * ph);

        ix = Math.Clamp(ix, 0, pw - 1);
        iy = Math.Clamp(iy, 0, ph - 1);
        iW = Math.Clamp(iW, 1, pw - ix);
        iH = Math.Clamp(iH, 1, ph - iy);

        return new Int32Rect(ix, iy, iW, iH);
    }

    private Rect ScreenRectOf(Int32Rect cr)
    {
        if (cr.IsEmpty) return Rect.Empty;
        var (ox, oy, iw, ih) = GetImageBounds();
        var pw = (double)ImagePixelWidth!.Value;
        var ph = (double)ImagePixelHeight!.Value;
        return new Rect(
            ox + cr.X / pw * iw,
            oy + cr.Y / ph * ih,
            cr.Width  / pw * iw,
            cr.Height / ph * ih);
    }
}
