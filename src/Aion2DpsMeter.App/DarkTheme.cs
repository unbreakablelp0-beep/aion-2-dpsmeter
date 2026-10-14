using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace Aion2DpsMeter.App;

/// <summary>
/// Dunkle Darstellung für die Fenster des Programms (nur Optik): dunkle Titelleiste über die Windows-Einstellung
/// „Immersive Dark Mode“ (DwmSetWindowAttribute), dunkle abgerundete Knöpfe mit Hover und Kontrollkästchen als
/// Schiebeschalter. Wirkt nur auf eigene Fenster.
/// </summary>
public static class DarkTheme
{
    private const int DwmUseImmersiveDarkMode = 20; // ab Windows 10 20H1
    private const int DwmUseImmersiveDarkModeOld = 19; // ältere Windows-10-Versionen

    // Je Thread ein eigenes Wörterbuch: WPF-Objekte gehören dem Thread, der sie erzeugt (im Programm gibt es nur einen
    // Oberflächen-Thread, in Tests mehrere).
    private static readonly ThreadLocal<ResourceDictionary> Styles = new(() => (ResourceDictionary)XamlReader.Parse(StylesXaml));

    /// <summary>Stile einhängen und die Titelleiste dunkel schalten.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Resources.MergedDictionaries.Add(Styles.Value!);

        // Über dem Spiel bleiben: Das Overlay nimmt nie den Fokus, daher darf Windows unsere Fenster nicht in den
        // Vordergrund holen – ohne „immer oben“ lägen sie hinter dem Spiel (Rückmeldung des Besitzers).
        window.Topmost = true;
        window.ShowActivated = true;
        window.Loaded += (_, _) => window.Activate();
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) DarkTitleBar(window);
        else window.SourceInitialized += (_, _) => DarkTitleBar(window);
    }

    /// <summary>Kleine Eingabe-Dialoge: dunkler Hintergrund, heller Text, dunkle Knöpfe und Titelleiste.</summary>
    public static void ApplyDialog(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        dialog.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x18, 0x1F));
        dialog.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0xE8, 0xEE));
        dialog.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        Apply(dialog);
    }

    /// <summary>Für Tests: die Stile (ohne Fenster).</summary>
    public static ResourceDictionary StyleResources => Styles.Value!;

    private static void DarkTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var on = 1;
        try
        {
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeOld, ref on, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Ohne DWM bleibt die Titelleiste hell – nur Optik.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const string StylesXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="#E6E8EE"/>
            <Setter Property="Background" Value="#262A35"/>
            <Setter Property="BorderBrush" Value="#3A3F4B"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,4"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}"
                          SnapsToDevicePixels="True">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#323746"/>
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#7B5CD6"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#3E3560"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#7B5CD6"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.45"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Kontrollkästchen als Schiebeschalter; der Text steht rechts daneben. -->
          <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#E6E8EE"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                  <StackPanel Orientation="Horizontal" Background="Transparent">
                    <Border x:Name="Track" Width="36" Height="20" CornerRadius="10" Background="#3A3F4B" VerticalAlignment="Center">
                      <Ellipse x:Name="Knob" Width="14" Height="14" Fill="#C8CCD6" HorizontalAlignment="Left" Margin="3,0,0,0"/>
                    </Border>
                    <ContentPresenter Margin="8,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                  </StackPanel>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="Track" Property="Background" Value="#7B5CD6"/>
                      <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/>
                      <Setter TargetName="Knob" Property="Margin" Value="0,0,3,0"/>
                      <Setter TargetName="Knob" Property="Fill" Value="White"/>
                    </Trigger>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Track" Property="Opacity" Value="0.85"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.45"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <!-- Eingabefelder dunkel mit Akzentrand bei Fokus. -->
          <Style TargetType="TextBox">
            <Setter Property="Foreground" Value="#E6E8EE"/>
            <Setter Property="Background" Value="#1C1F28"/>
            <Setter Property="BorderBrush" Value="#3A3F4B"/>
            <Setter Property="CaretBrush" Value="#E6E8EE"/>
            <Setter Property="Padding" Value="6,3"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6">
                    <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#7B5CD6"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Auswahllisten dunkel: Knopf mit Pfeil, Liste als dunkles Aufklappfeld. -->
          <Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="#E6E8EE"/>
            <Setter Property="Padding" Value="8,4"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                  <Border x:Name="Bd" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}">
                    <ContentPresenter/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#3E3560"/>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#2E2A48"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="#E6E8EE"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                  <Grid>
                    <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press"
                                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                      <ToggleButton.Template>
                        <ControlTemplate TargetType="ToggleButton">
                          <Border x:Name="Bd" Background="#1C1F28" BorderBrush="#3A3F4B" BorderThickness="1" CornerRadius="6">
                            <TextBlock Text="▾" Foreground="#9AA0AE" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,8,0"/>
                          </Border>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                              <Setter TargetName="Bd" Property="BorderBrush" Value="#7B5CD6"/>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter IsHitTestVisible="False" Margin="8,4,24,4" VerticalAlignment="Center"
                                      Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
                    <Popup IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                      <Border Background="#1C1F28" BorderBrush="#3A3F4B" BorderThickness="1" CornerRadius="6" Padding="4"
                              MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                        <ScrollViewer MaxHeight="260" VerticalScrollBarVisibility="Auto">
                          <ItemsPresenter/>
                        </ScrollViewer>
                      </Border>
                    </Popup>
                  </Grid>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Regler: dünne Bahn, Akzent-Füllung über den Daumen. -->
          <Style TargetType="Slider">
            <Setter Property="Foreground" Value="#7B5CD6"/>
            <Setter Property="Background" Value="#3A3F4B"/>
          </Style>
        </ResourceDictionary>
        """;
}
