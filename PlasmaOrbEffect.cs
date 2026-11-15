/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Elektra.UI.Effects;

public sealed class PlasmaOrbEffect : ShaderEffect
{
    private static readonly PixelShader? Shader = TryLoadShader();

    private static PixelShader? TryLoadShader()
    {
        try
        {
            return new PixelShader
            {
                UriSource = new Uri("pack://application:,,,/Elektra;component/UI/Effects/PlasmaOrbEffect.ps", UriKind.Absolute)
            };
        }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        {
            Trace.TraceWarning("Could not load the plasma shader; using the standard orb visuals. {0}", ex);
            return null;
        }
    }

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(PlasmaOrbEffect), 0);

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(nameof(Time), typeof(double), typeof(PlasmaOrbEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty AudioEnergyProperty =
        DependencyProperty.Register(nameof(AudioEnergy), typeof(double), typeof(PlasmaOrbEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty PrimaryColorProperty =
        DependencyProperty.Register(nameof(PrimaryColor), typeof(Color), typeof(PlasmaOrbEffect),
            new UIPropertyMetadata(Color.FromRgb(124, 58, 237), PixelShaderConstantCallback(2)));

    public static readonly DependencyProperty SecondaryColorProperty =
        DependencyProperty.Register(nameof(SecondaryColor), typeof(Color), typeof(PlasmaOrbEffect),
            new UIPropertyMetadata(Color.FromRgb(196, 181, 253), PixelShaderConstantCallback(3)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public double AudioEnergy { get => (double)GetValue(AudioEnergyProperty); set => SetValue(AudioEnergyProperty, value); }
    public Color PrimaryColor { get => (Color)GetValue(PrimaryColorProperty); set => SetValue(PrimaryColorProperty, value); }
    public Color SecondaryColor { get => (Color)GetValue(SecondaryColorProperty); set => SetValue(SecondaryColorProperty, value); }

    public PlasmaOrbEffect()
    {
        if (Shader != null)
        {
            PixelShader = Shader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(TimeProperty);
            UpdateShaderValue(AudioEnergyProperty);
            UpdateShaderValue(PrimaryColorProperty);
            UpdateShaderValue(SecondaryColorProperty);
        }
    }
}