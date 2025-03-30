using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SolisManager.Client.Constants;
using SolisManager.Shared.Models;

namespace SolisManager.Shared;

public static class Utils
{
    public static string? DisplayDate(this DateTime dateTime)
    {
        return $"{dateTime.ToLocalTime():dd-MMM-yyyy}";
    }

    public static string? DisplayDateTime(this DateTime dateTime)
    {
        return $"{dateTime.ToLocalTime():dd-MMM-yyyy HH:mm}";
    }

    public static void CopyPropertiesTo<T, TU>(this T source, TU dest)
    {
        var sourceProps = typeof (T).GetProperties().Where(x => x.CanRead).ToList();
        var destProps = typeof(TU).GetProperties()
            .Where(x => x.CanWrite)
            .ToList();

        foreach (var sourceProp in sourceProps)
        {
            if (destProps.Any(x => x.Name == sourceProp.Name))
            {
                var p = destProps.First(x => x.Name == sourceProp.Name);
                if(p.CanWrite) { // check if the property can be set or no.
                    p.SetValue(dest, sourceProp.GetValue(source, null), null);
                }
            }

        }

    }

    public static T? Clone<T>(this T source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json);
    }
    
    /// <summary>
    /// To identify the product code for a particular tariff, you can usually take off the first few letters of
    /// the tariff (E-1R-, E-2R- or G-1R) which indicate if it is electricity single register, electricity dual
    /// register (eg economy7) or gas single register, and the letter at the end (eg -A) which indicates the
    /// region code. So, for example, E-1R-VAR-19-04-12-N is one of the tariffs for product VAR-19-04-12.
    /// </summary>
    /// <param name="tariffCode"></param>
    /// <returns></returns>
    public static string GetProductFromTariffCode(this string tariffCode)
    {
        if (string.IsNullOrEmpty(tariffCode))
            return string.Empty;
        
        var lastDash = tariffCode.LastIndexOf('-');
        if( lastDash > 0 )
            tariffCode = tariffCode.Substring(0, lastDash);

        // Hacky, but we don't do it very often, so meh
        var first = tariffCode.IndexOf('-');
        if (first > 0)
        {
            tariffCode = tariffCode.Substring(first + 1);
            var second = tariffCode.IndexOf('-');
            if (second > 0)
            {
                return tariffCode.Substring(second + 1);
            }
        }

        return string.Empty;
    }

    public static void InitMudServices(this IServiceCollection services)
    {
        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.SnackbarVariant = UIConstants.MudVariant;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.ShowTransitionDuration = 250;
            config.SnackbarConfiguration.HideTransitionDuration = 150;
        });
    }
}