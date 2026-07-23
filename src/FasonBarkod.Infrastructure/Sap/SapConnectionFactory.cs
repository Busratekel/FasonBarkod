using FasonBarkod.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SapNwRfc;

namespace FasonBarkod.Infrastructure.Sap;

public interface ISapConnectionFactory
{
    bool IsConfigured { get; }

    SapConnection OpenConnection();
}

public class SapConnectionFactory(IOptions<SapOptions> options, ILogger<SapConnectionFactory> logger) : ISapConnectionFactory
{
    public bool IsConfigured => options.Value.IsConfigured;

    public SapConnection OpenConnection()
    {
        var sap = options.Value;
        if (!sap.IsConfigured)
        {
            throw new InvalidOperationException("SAP bağlantı ayarları eksik. User Secrets veya appsettings kontrol edin.");
        }

        var connectionString =
            $"AppServerHost={sap.AppServerHost}; " +
            $"SystemNumber={sap.SystemNumber}; " +
            $"User={sap.User}; " +
            $"Password={sap.Password}; " +
            $"Client={sap.Client}; " +
            $"Language={sap.Language}; " +
            "PoolSize=5; Trace=0";

        logger.LogDebug("SAP bağlantısı: {Host} client {Client}", sap.AppServerHost, sap.Client);

        var connection = new SapConnection(connectionString);
        connection.Connect();
        return connection;
    }
}
