using MudBlazor;

namespace AMS.Web.Services
{
    /// <summary>Same look as the UMS admin portal: dark shiny prismatic blue for navigation, strips and lines; white work areas (see wwwroot/css/app.css).</summary>
    public static class AmsTheme
    {
        public static readonly MudTheme Theme = new()
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#1559e0",
                PrimaryDarken = "#0f3fae",
                PrimaryLighten = "#4f9bff",
                Secondary = "#0c2c80",
                Tertiary = "#4f9bff",
                Info = "#2a78f0",
                Success = "#0b7a33",
                Warning = "#b26a00",
                Error = "#c62828",
                Background = "#f4f7fd",
                Surface = "#ffffff",
                AppbarBackground = "#ffffff",
                AppbarText = "#0a1f5c",
                DrawerBackground = "#0a1f5c",
                DrawerText = "#d5e2ff",
                DrawerIcon = "#a9c3f5",
                TextPrimary = "#0f1d3d",
                TextSecondary = "#5d6b8a",
                LinesDefault = "#dce5f5",
                TableLines = "#e8eef9",
                TableHover = "#f5f9ff",
                ActionDefault = "#0c2c80",
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = new[] { "Inter", "Segoe UI", "Roboto", "sans-serif" }, FontSize = ".875rem" },
                H4 = new H4Typography { FontWeight = "700", FontSize = "1.6rem" },
                H5 = new H5Typography { FontWeight = "700", FontSize = "1.3rem" },
                H6 = new H6Typography { FontWeight = "650", FontSize = "1rem" },
                Button = new ButtonTypography { TextTransform = "none", FontWeight = "600" },
            },
            LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px", DrawerWidthLeft = "260px" },
        };
    }
}
