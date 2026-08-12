using Avalonia.Media;

namespace WifiSender.DesignSystem;

public static class Colors
{
    // Primary Brand & Gradients
    public static readonly IBrush Primary = new SolidColorBrush(Color.Parse("#007AFF"));
    public static readonly IBrush PrimaryDark = new SolidColorBrush(Color.Parse("#0A84FF"));
    public static readonly IBrush PrimaryContainer = new SolidColorBrush(Color.Parse("#E3F2FD"));
    public static readonly IBrush OnPrimary = new SolidColorBrush(Color.Parse("#FFFFFF"));

    // Vibrant Accents
    public static readonly IBrush AccentCyan = new SolidColorBrush(Color.Parse("#00C7BE"));
    public static readonly IBrush AccentCyanDark = new SolidColorBrush(Color.Parse("#64D2FF"));
    public static readonly IBrush AccentPurple = new SolidColorBrush(Color.Parse("#AF52DE"));
    public static readonly IBrush AccentPurpleDark = new SolidColorBrush(Color.Parse("#BF5AF2"));
    public static readonly IBrush AccentIndigo = new SolidColorBrush(Color.Parse("#5856D6"));
    public static readonly IBrush AccentIndigoDark = new SolidColorBrush(Color.Parse("#5E5CE6"));
    public static readonly IBrush AccentPink = new SolidColorBrush(Color.Parse("#FF2D55"));
    public static readonly IBrush AccentPinkDark = new SolidColorBrush(Color.Parse("#FF375F"));

    // Status Colors
    public static readonly IBrush Success = new SolidColorBrush(Color.Parse("#34C759"));
    public static readonly IBrush SuccessDark = new SolidColorBrush(Color.Parse("#30D158"));
    public static readonly IBrush SuccessContainer = new SolidColorBrush(Color.Parse("#E8F8ED"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FF9500"));
    public static readonly IBrush WarningDark = new SolidColorBrush(Color.Parse("#FF9F0A"));
    public static readonly IBrush WarningContainer = new SolidColorBrush(Color.Parse("#FFF4E5"));
    public static readonly IBrush Error = new SolidColorBrush(Color.Parse("#FF3B30"));
    public static readonly IBrush ErrorDark = new SolidColorBrush(Color.Parse("#FF453A"));
    public static readonly IBrush ErrorContainer = new SolidColorBrush(Color.Parse("#FFE8E6"));
    public static readonly IBrush Info = new SolidColorBrush(Color.Parse("#5AC8FA"));
    public static readonly IBrush InfoContainer = new SolidColorBrush(Color.Parse("#E5F6FF"));

    // Neutral Light Palette
    public static readonly IBrush Background = new SolidColorBrush(Color.Parse("#F8FAFC"));
    public static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static readonly IBrush SurfaceVariant = new SolidColorBrush(Color.Parse("#F1F5F9"));
    public static readonly IBrush SurfaceElevated = new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#0F172A"));
    public static readonly IBrush TextSecondary = new SolidColorBrush(Color.Parse("#475569"));
    public static readonly IBrush TextTertiary = new SolidColorBrush(Color.Parse("#94A3B8"));
    public static readonly IBrush TextOnPrimary = new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static readonly IBrush Border = new SolidColorBrush(Color.Parse("#E2E8F0"));
    public static readonly IBrush BorderStrong = new SolidColorBrush(Color.Parse("#CBD5E1"));

    // Neutral Dark Palette (Deep Obsidian & Glass Surface)
    public static readonly IBrush DarkBackground = new SolidColorBrush(Color.Parse("#0A0C10"));
    public static readonly IBrush DarkSurface = new SolidColorBrush(Color.Parse("#141720"));
    public static readonly IBrush DarkSurfaceVariant = new SolidColorBrush(Color.Parse("#1D2230"));
    public static readonly IBrush DarkSurfaceElevated = new SolidColorBrush(Color.Parse("#262C3D"));
    public static readonly IBrush DarkTextPrimary = new SolidColorBrush(Color.Parse("#F8FAFC"));
    public static readonly IBrush DarkTextSecondary = new SolidColorBrush(Color.Parse("#94A3B8"));
    public static readonly IBrush DarkTextTertiary = new SolidColorBrush(Color.Parse("#64748B"));
    public static readonly IBrush DarkBorder = new SolidColorBrush(Color.Parse("#2A3042"));
    public static readonly IBrush DarkBorderStrong = new SolidColorBrush(Color.Parse("#3B4359"));
}
