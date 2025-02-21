using System.Reflection;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Services;

public class UserAgentProvider : IUserAgentProvider
{
    public string UserAgent
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"SolisAgileManager/{version}";
        }
    }
}