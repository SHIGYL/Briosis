using Brio.Config;
using EmbedIO;
using EmbedIO.WebApi;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Brio.Web;

public class WebService : IDisposable
{
    private const string WebServerUrl = "http://localhost:42429/";
    private const string WebApiRoute = "/briosis";

    public bool IsRunning => _shouldBeRunning && _webServer != null && _webServer.State == WebServerState.Listening;

    private bool _shouldBeRunning = false;
    private WebServer? _webServer;

    private readonly ConfigurationService _configurationService;
    private readonly IServiceProvider _serviceProvider;

    public WebService(ConfigurationService configurationService, IServiceProvider serviceProvider)
    {
        _configurationService = configurationService;
        _serviceProvider = serviceProvider;
        RefreshState();

        _configurationService.OnConfigurationChanged += OnConfigurationChanged;
    }

    private void RefreshState()
    {
        var allow = _configurationService.Configuration.IPC.AllowWebAPI;
        if(allow != _shouldBeRunning)
        {
            _shouldBeRunning = allow;

            if(_shouldBeRunning)
            {
                CreateWebServer();
            }
            else
            {
                DestroyWebServer();
            }
        }
    }

    private void CreateWebServer()
    {
        DestroyWebServer();

        try
        {
            var server = new WebServer(o => o
            .WithUrlPrefix(WebServerUrl)
            .WithMode(HttpListenerMode.EmbedIO))
             .WithWebApi(WebApiRoute, m => m.WithController(() => ActivatorUtilities.CreateInstance<ActorWebController>(_serviceProvider))
            );

            server.Start();

            _webServer = server;
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, "Failed to start webserver");
            _webServer = null;
        }
    }

    private void DestroyWebServer()
    {
        _webServer?.Dispose();
        _webServer = null;
    }

    private void OnConfigurationChanged()
    {
        RefreshState();
    }

    public void Dispose()
    {
        _configurationService.OnConfigurationChanged -= OnConfigurationChanged;
        DestroyWebServer();
    }
}
