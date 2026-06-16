using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FileExplorer.Helpers;

/// <summary>
/// Two-layer soft-body jelly: outer shell shifts and tilts as a rigid slab;
/// inner content squashes, shears, and bulges so the whole surface wobbles — not just corners.
/// </summary>
public sealed class WindowJellyDragEffect
{
    private const double Stiffness = 68.0;
    private const double Damping = 6.8;
    private const double Mass = 2.4;
    private const double BodyMass = 4.2;
    private const double BulgeMass = 3.6;

    private const double InertialForceScale = 0.00115;
    private const double VelocityStretchScale = 0.000078;
    private const double AccelSkewScale = 0.00032;
    private const double VelocitySkewScale = 0.00016;
    private const double VelocityTranslateScale = 0.014;
    private const double VelocityRotateScale = 0.000048;
    private const double AccelRotateScale = 0.00014;
    private const double BulgeForceScale = 1.35;

    private const double MaxStretch = 0.10;
    private const double MaxSkew = 9.5;
    private const double MaxBodyTranslate = 36.0;
    private const double MaxBulgeTranslate = 22.0;
    private const double MaxRotate = 5.8;
    private const double CrossAxisSquash = 0.72;
    private const double OriginShift = 0.24;

    // Frame swells with shake intensity.
    private const double BaseBorderThickness = 1.0;
    private const double MaxExtraBorderThickness = 8.0;
    private const double BaseFramePadding = 0.0;
    private const double MaxExtraFramePadding = 6.0;
    private const double BaseCornerRadius = 10.0;
    private const double MaxExtraCornerRadius = 6.0;
    private const double BaseShadowBlur = 24.0;
    private const double MaxExtraShadowBlur = 28.0;
    private const double BaseShadowOpacity = 0.45;
    private const double MaxExtraShadowOpacity = 0.35;

    private const double SettlePositionEpsilon = 0.00028;
    private const double SettleVelocityEpsilon = 0.011;
    private const double MaxFrameDeltaSeconds = 1.0 / 30.0;

    private readonly FrameworkElement _outerShell;
    private readonly FrameworkElement _innerContent;
    private readonly Border? _shellBorder;
    private readonly DropShadowEffect? _shellShadow;

    private readonly TranslateTransform _bodyTranslate;
    private readonly RotateTransform _bodyRotate;
    private readonly ScaleTransform _contentScale;
    private readonly SkewTransform _contentSkew;
    private readonly TranslateTransform _contentBulge;

    private readonly SpringMass1D _stretchX = new();
    private readonly SpringMass1D _stretchY = new();
    private readonly SpringMass1D _skewX = new();
    private readonly SpringMass1D _skewY = new();
    private readonly SpringMass1D _bodyTranslateX = new();
    private readonly SpringMass1D _bodyTranslateY = new();
    private readonly SpringMass1D _bulgeX = new();
    private readonly SpringMass1D _bulgeY = new();
    private readonly SpringMass1D _rotate = new();

    private Window? _window;
    private Point _lastPosition;
    private long _lastKinematicTick;
    private long _lastFrameTick;
    private bool _active;
    private bool _simulating;
    private double _smoothedVelocityX;
    private double _smoothedVelocityY;
    private double _windowAccelX;
    private double _windowAccelY;
    private double _prevInstantVelocityX;
    private double _prevInstantVelocityY;

    /// <summary>1.0 = default strength. Scales deformation, forces, and frame pulse.</summary>
    public double Intensity { get; set; } = 1.0;

    public WindowJellyDragEffect(FrameworkElement outerShell, FrameworkElement innerContent)
    {
        _outerShell = outerShell;
        _innerContent = innerContent;

        _outerShell.RenderTransformOrigin = new Point(0.5, 0.5);
        _bodyTranslate = new TranslateTransform();
        _bodyRotate = new RotateTransform();
        _outerShell.RenderTransform = new TransformGroup
        {
            Children = { _bodyTranslate, _bodyRotate },
        };

        _innerContent.RenderTransformOrigin = new Point(0.5, 0.5);
        _contentScale = new ScaleTransform(1, 1);
        _contentSkew = new SkewTransform();
        _contentBulge = new TranslateTransform();
        _innerContent.RenderTransform = new TransformGroup
        {
            Children = { _contentScale, _contentSkew, _contentBulge },
        };

        if (outerShell is Border shell)
        {
            _shellBorder = shell;
            _shellShadow = new DropShadowEffect
            {
                BlurRadius = BaseShadowBlur,
                ShadowDepth = 0,
                Opacity = BaseShadowOpacity,
                Color = Colors.Black,
            };
            _shellBorder.Effect = _shellShadow;
        }
    }

    private bool _renderingSubscribed;

    public bool IsDragActive => _active;

    public void Begin(Window window)
    {
        if (_active || window.WindowState != WindowState.Normal)
            return;

        if (_simulating)
        {
            StopSimulation();
            _simulating = false;
            ResetSprings();
        }

        _window = window;
        _active = true;
        _simulating = true;
        _smoothedVelocityX = 0;
        _smoothedVelocityY = 0;
        _windowAccelX = 0;
        _windowAccelY = 0;
        _prevInstantVelocityX = 0;
        _prevInstantVelocityY = 0;
        _lastPosition = new Point(window.Left, window.Top);
        _lastKinematicTick = Environment.TickCount64;
        _lastFrameTick = _lastKinematicTick;
        ResetSprings();

        window.LocationChanged += OnLocationChanged;
        StartSimulation();
    }

    public void End()
    {
        if (!_active)
            return;

        _active = false;
        if (_window is not null)
            _window.LocationChanged -= OnLocationChanged;

        _simulating = !AllSpringsSettled();
        if (!_simulating)
        {
            ResetSprings();
            ApplyTransform();
            StopSimulation();
        }
    }

    public void Reset()
    {
        _active = false;
        _simulating = false;
        if (_window is not null)
        {
            _window.LocationChanged -= OnLocationChanged;
            _window = null;
        }

        ResetSprings();
        ApplyTransform();
        StopSimulation();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_window is null || _window.WindowState != WindowState.Normal)
            return;

        var now = Environment.TickCount64;
        var position = new Point(_window.Left, _window.Top);
        var deltaSeconds = Math.Max(0.001, (now - _lastKinematicTick) / 1000.0);

        var instantVelocityX = (position.X - _lastPosition.X) / deltaSeconds;
        var instantVelocityY = (position.Y - _lastPosition.Y) / deltaSeconds;

        _smoothedVelocityX = Lerp(_smoothedVelocityX, instantVelocityX, 0.26);
        _smoothedVelocityY = Lerp(_smoothedVelocityY, instantVelocityY, 0.26);

        _windowAccelX = (instantVelocityX - _prevInstantVelocityX) / deltaSeconds;
        _windowAccelY = (instantVelocityY - _prevInstantVelocityY) / deltaSeconds;

        _prevInstantVelocityX = instantVelocityX;
        _prevInstantVelocityY = instantVelocityY;
        _lastPosition = position;
        _lastKinematicTick = now;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_simulating)
            return;

        var now = Environment.TickCount64;
        var deltaSeconds = Math.Min(MaxFrameDeltaSeconds, Math.Max(0.001, (now - _lastFrameTick) / 1000.0));
        _lastFrameTick = now;

        StepSprings(deltaSeconds, includeDragForces: _active);
        ApplyTransform();

        if (_active || !AllSpringsSettled())
            return;

        _simulating = false;
        ResetSprings();
        ApplyTransform();
        StopSimulation();
    }

    private void StepSprings(double deltaSeconds, bool includeDragForces)
    {
        double dragStretchX = 0;
        double dragStretchY = 0;
        double dragSkewX = 0;
        double dragSkewY = 0;
        double dragBodyTranslateX = 0;
        double dragBodyTranslateY = 0;
        double dragBulgeX = 0;
        double dragBulgeY = 0;
        double dragRotate = 0;

        if (includeDragForces)
        {
            var i = Intensity;

            dragBodyTranslateX = -_windowAccelX * InertialForceScale * i;
            dragBodyTranslateY = -_windowAccelY * InertialForceScale * i;

            dragStretchX = _smoothedVelocityX * VelocityStretchScale * i;
            dragStretchY = _smoothedVelocityY * VelocityStretchScale * i;

            dragSkewX = (_smoothedVelocityX * VelocitySkewScale + _windowAccelX * AccelSkewScale) * i;
            dragSkewY = (_smoothedVelocityY * VelocitySkewScale + _windowAccelY * AccelSkewScale) * i;

            dragBodyTranslateX += -_smoothedVelocityX * VelocityTranslateScale * i;
            dragBodyTranslateY += -_smoothedVelocityY * VelocityTranslateScale * i;

            dragBulgeX = dragBodyTranslateX * BulgeForceScale + _windowAccelY * AccelSkewScale * 0.55 * i;
            dragBulgeY = dragBodyTranslateY * BulgeForceScale - _windowAccelX * AccelSkewScale * 0.55 * i;

            dragRotate = (_smoothedVelocityX * VelocityRotateScale
                + _windowAccelX * AccelRotateScale
                - _smoothedVelocityY * VelocityRotateScale * 0.15) * i;
        }

        _stretchX.Integrate(deltaSeconds, Stiffness, Damping, Mass, dragStretchX);
        _stretchY.Integrate(deltaSeconds, Stiffness, Damping, Mass, dragStretchY);
        _skewX.Integrate(deltaSeconds, Stiffness * 0.82, Damping * 0.82, Mass * 1.15, dragSkewX);
        _skewY.Integrate(deltaSeconds, Stiffness * 0.82, Damping * 0.82, Mass * 1.15, dragSkewY);
        _bodyTranslateX.Integrate(deltaSeconds, Stiffness * 0.42, Damping * 0.58, BodyMass, dragBodyTranslateX);
        _bodyTranslateY.Integrate(deltaSeconds, Stiffness * 0.42, Damping * 0.58, BodyMass, dragBodyTranslateY);
        _bulgeX.Integrate(deltaSeconds, Stiffness * 0.55, Damping * 0.64, BulgeMass, dragBulgeX);
        _bulgeY.Integrate(deltaSeconds, Stiffness * 0.55, Damping * 0.64, BulgeMass, dragBulgeY);
        _rotate.Integrate(deltaSeconds, Stiffness * 0.72, Damping * 0.76, Mass * 1.4, dragRotate);
    }

    private void ApplyTransform()
    {
        var i = Math.Max(0, Intensity);
        var maxStretch = MaxStretch * i;
        var maxSkew = MaxSkew * i;
        var maxBodyTranslate = MaxBodyTranslate * i;
        var maxBulgeTranslate = MaxBulgeTranslate * i;
        var maxRotate = MaxRotate * i;

        var stretchX = Math.Clamp(_stretchX.Position, -maxStretch, maxStretch);
        var stretchY = Math.Clamp(_stretchY.Position, -maxStretch, maxStretch);

        _bodyTranslate.X = Math.Clamp(_bodyTranslateX.Position, -maxBodyTranslate, maxBodyTranslate);
        _bodyTranslate.Y = Math.Clamp(_bodyTranslateY.Position, -maxBodyTranslate, maxBodyTranslate);
        _bodyRotate.Angle = Math.Clamp(_rotate.Position, -maxRotate, maxRotate);

        _contentScale.ScaleX = 1 + stretchX - stretchY * CrossAxisSquash;
        _contentScale.ScaleY = 1 + stretchY - stretchX * CrossAxisSquash;
        _contentSkew.AngleX = Math.Clamp(_skewX.Position, -maxSkew, maxSkew);
        _contentSkew.AngleY = Math.Clamp(_skewY.Position, -maxSkew, maxSkew);
        _contentBulge.X = Math.Clamp(_bulgeX.Position, -maxBulgeTranslate, maxBulgeTranslate);
        _contentBulge.Y = Math.Clamp(_bulgeY.Position, -maxBulgeTranslate, maxBulgeTranslate);

        UpdateContentTransformOrigin();
        ApplyBorderPulse(ComputeShakeIntensity());
    }

    private double ComputeShakeIntensity()
    {
        static double N(double value, double max) => max <= 0 ? 0 : Math.Abs(value) / max;

        var positional =
            N(_stretchX.Position, MaxStretch)
            + N(_stretchY.Position, MaxStretch)
            + N(_skewX.Position, MaxSkew) * 0.45
            + N(_skewY.Position, MaxSkew) * 0.45
            + N(_bodyTranslateX.Position, MaxBodyTranslate) * 0.55
            + N(_bodyTranslateY.Position, MaxBodyTranslate) * 0.55
            + N(_bulgeX.Position, MaxBulgeTranslate) * 0.5
            + N(_bulgeY.Position, MaxBulgeTranslate) * 0.5
            + N(_rotate.Position, MaxRotate) * 0.4;

        var kinetic =
            N(_stretchX.Velocity, 0.09)
            + N(_stretchY.Velocity, 0.09)
            + N(_skewX.Velocity, 2.8)
            + N(_skewY.Velocity, 2.8)
            + N(_bodyTranslateX.Velocity, 3.2)
            + N(_bodyTranslateY.Velocity, 3.2)
            + N(_bulgeX.Velocity, 2.6)
            + N(_bulgeY.Velocity, 2.6)
            + N(_rotate.Velocity, 1.4);

        return Math.Clamp(positional * 0.5 + kinetic * 0.5, 0, 1);
    }

    private void ApplyBorderPulse(double rawIntensity)
    {
        if (_shellShadow is null)
            return;

        var shake = Math.Clamp(rawIntensity, 0, 1);
        var eased = shake * shake * (3 - 2 * shake);
        var scale = Math.Max(0, Intensity);

        _shellShadow.BlurRadius = Math.Max(0, BaseShadowBlur + eased * MaxExtraShadowBlur * scale);
        _shellShadow.Opacity = Math.Clamp(
            BaseShadowOpacity + eased * MaxExtraShadowOpacity * Math.Min(1, scale),
            0,
            1);
    }

    private void ResetBorderAppearance()
    {
        if (_shellShadow is null)
            return;

        _shellShadow.BlurRadius = BaseShadowBlur;
        _shellShadow.Opacity = BaseShadowOpacity;
    }

    private void UpdateContentTransformOrigin()
    {
        var speed = Math.Sqrt(_smoothedVelocityX * _smoothedVelocityX + _smoothedVelocityY * _smoothedVelocityY);
        if (speed < 8)
        {
            _innerContent.RenderTransformOrigin = new Point(0.5, 0.5);
            return;
        }

        var nx = _smoothedVelocityX / speed;
        var ny = _smoothedVelocityY / speed;
        var ox = 0.5 - nx * OriginShift;
        var oy = 0.5 - ny * OriginShift;
        _innerContent.RenderTransformOrigin = new Point(
            Math.Clamp(ox, 0.18, 0.82),
            Math.Clamp(oy, 0.18, 0.82));
    }

    private bool AllSpringsSettled() =>
        _stretchX.IsSettled(SettlePositionEpsilon, SettleVelocityEpsilon)
        && _stretchY.IsSettled(SettlePositionEpsilon, SettleVelocityEpsilon)
        && _skewX.IsSettled(SettlePositionEpsilon * 8, SettleVelocityEpsilon * 4)
        && _skewY.IsSettled(SettlePositionEpsilon * 8, SettleVelocityEpsilon * 4)
        && _bodyTranslateX.IsSettled(SettlePositionEpsilon * 40, SettleVelocityEpsilon * 3)
        && _bodyTranslateY.IsSettled(SettlePositionEpsilon * 40, SettleVelocityEpsilon * 3)
        && _bulgeX.IsSettled(SettlePositionEpsilon * 35, SettleVelocityEpsilon * 3)
        && _bulgeY.IsSettled(SettlePositionEpsilon * 35, SettleVelocityEpsilon * 3)
        && _rotate.IsSettled(SettlePositionEpsilon * 60, SettleVelocityEpsilon * 4);

    private void ResetSprings()
    {
        _stretchX.Reset();
        _stretchY.Reset();
        _skewX.Reset();
        _skewY.Reset();
        _bodyTranslateX.Reset();
        _bodyTranslateY.Reset();
        _bulgeX.Reset();
        _bulgeY.Reset();
        _rotate.Reset();
        _innerContent.RenderTransformOrigin = new Point(0.5, 0.5);
        ResetBorderAppearance();
    }

    private void StartSimulation()
    {
        if (_renderingSubscribed)
            return;

        CompositionTarget.Rendering += OnRendering;
        _renderingSubscribed = true;
    }

    private void StopSimulation()
    {
        if (!_renderingSubscribed)
            return;

        CompositionTarget.Rendering -= OnRendering;
        _renderingSubscribed = false;
    }

    private static double Lerp(double from, double to, double amount) =>
        from + (to - from) * amount;

    private sealed class SpringMass1D
    {
        public double Position { get; private set; }
        public double Velocity { get; private set; }

        public void Integrate(double deltaSeconds, double stiffness, double damping, double mass, double externalForce)
        {
            var springForce = -stiffness * Position;
            var dampingForce = -damping * Velocity;
            var acceleration = (springForce + dampingForce + externalForce) / mass;

            Velocity += acceleration * deltaSeconds;
            Position += Velocity * deltaSeconds;
        }

        public bool IsSettled(double positionEpsilon, double velocityEpsilon) =>
            Math.Abs(Position) < positionEpsilon && Math.Abs(Velocity) < velocityEpsilon;

        public void Reset()
        {
            Position = 0;
            Velocity = 0;
        }
    }
}
