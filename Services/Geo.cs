namespace product_website.Services;

/// <summary>Location rules shared by the report endpoints.</summary>
public static class Geo
{
    // A box around Hadzici, Ilidza, Novi Grad, Novo Sarajevo, Centar, Stari Grad and Vogosca.
    // Reports outside it are rejected. Keep in sync with lib/config/map_config.dart in the app.
    private const double MinLat = 43.72, MaxLat = 43.99;
    private const double MinLng = 18.12, MaxLng = 18.56;

    /// <summary>How close (in metres) an open report of the same category must be to count as "similar".</summary>
    public const double NearbyRadiusMeters = 60;

    public static bool IsInSarajevo(double lat, double lng) =>
        lat >= MinLat && lat <= MaxLat && lng >= MinLng && lng <= MaxLng;
}
