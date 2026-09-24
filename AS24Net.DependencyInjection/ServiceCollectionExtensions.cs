using Havit.Data.EntityFrameworkCore;
using Havit.Data.EntityFrameworkCore.Patterns.DependencyInjection;
using Havit.Data.EntityFrameworkCore.Patterns.Infrastructure;
using Havit.Data.EntityFrameworkCore.Patterns.Repositories;
using Havit.Data.EntityFrameworkCore.Patterns.UnitOfWorks.EntityValidation;
using Havit.Data.Patterns.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Entity;
using Havit.Data.Patterns.Repositories;
using Havit.Services.Caching;
using Havit.Services.TimeServices;
using AS24Net.Services;

namespace AS24Net.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <param name="enableSensitiveDataLogging">Log SQL parameter values; enable in development only.</param>
    public static IServiceCollection AddDataLayer(this IServiceCollection services,
        IConfiguration configuration, bool enableSensitiveDataLogging = false)
    {
        var connString = configuration.GetConnectionString("AS24Net");
        if (string.IsNullOrWhiteSpace(connString))
            throw new InvalidOperationException("Connection string 'AS24Net' is not configured.");

        services.AddDbContext<IDbContext, A24DbContext>(options => options
            .UseNpgsql(connString)
            .EnableSensitiveDataLogging(enableSensitiveDataLogging));
        
        services.AddDataLayerCoreServices();
        
        //Connection
        services.TryAddScoped<IConnectionRepository, ConnectionRepository>();
        services.TryAddScoped<IRepository<Connection, int>>(sp => sp.GetRequiredService<IConnectionRepository>());
        services.TryAddTransient<IEntityKeyAccessor<Connection, int>, DbEntityKeyAccessor<Connection, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<Connection, int>, ConnectionDbRepositoryQueryProvider>();

        //Partner
        services.TryAddScoped<IPartnerRepository, PartnerRepository>();
        services.TryAddScoped<IRepository<Partner, int>>(sp => sp.GetRequiredService<IPartnerRepository>());
        services.TryAddTransient<IEntityKeyAccessor<Partner, int>, DbEntityKeyAccessor<Partner, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<Partner, int>, PartnerDbRepositoryQueryProvider>();

        //Identity
        services.TryAddScoped<IIdentityRepository, IdentityRepository>();
        services.TryAddScoped<IRepository<Identity, int>>(sp => sp.GetRequiredService<IIdentityRepository>());
        services.TryAddTransient<IEntityKeyAccessor<Identity, int>, DbEntityKeyAccessor<Identity, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<Identity, int>, IdentityDbRepositoryQueryProvider>();

        //Certificate
        services.TryAddScoped<ICertificateRepository, CertificateRepository>();
        services.TryAddScoped<IRepository<Certificate, int>>(sp => sp.GetRequiredService<ICertificateRepository>());
        services.TryAddTransient<IEntityKeyAccessor<Certificate, int>, DbEntityKeyAccessor<Certificate, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<Certificate, int>, CertificateDbRepositoryQueryProvider>();

        //CertificateChange
        services.TryAddScoped<ICertificateChangeRepository, CertificateChangeRepository>();
        services.TryAddScoped<IRepository<CertificateChange, int>>(sp => sp.GetRequiredService<ICertificateChangeRepository>());
        services.TryAddTransient<IEntityKeyAccessor<CertificateChange, int>, DbEntityKeyAccessor<CertificateChange, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<CertificateChange, int>, CertificateChangeDbRepositoryQueryProvider>();

        //OutgoingMessage
        services.TryAddScoped<IOutgoingMessageRepository, OutgoingMessageRepository>();
        services.TryAddScoped<IRepository<OutgoingMessage, int>>(sp => sp.GetRequiredService<IOutgoingMessageRepository>());
        services.TryAddTransient<IEntityKeyAccessor<OutgoingMessage, int>, DbEntityKeyAccessor<OutgoingMessage, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<OutgoingMessage, int>, OutgoingMessageDbRepositoryQueryProvider>();

        //ReceivedMessage
        services.TryAddScoped<IReceivedMessageRepository, ReceivedMessageRepository>();
        services.TryAddScoped<IRepository<ReceivedMessage, int>>(sp => sp.GetRequiredService<IReceivedMessageRepository>());
        services.TryAddTransient<IEntityKeyAccessor<ReceivedMessage, int>, DbEntityKeyAccessor<ReceivedMessage, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<ReceivedMessage, int>, ReceivedMessageDbRepositoryQueryProvider>();

        //ApiToken
        services.TryAddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.TryAddScoped<IRepository<ApiToken, int>>(sp => sp.GetRequiredService<IApiTokenRepository>());
        services.TryAddTransient<IEntityKeyAccessor<ApiToken, int>, DbEntityKeyAccessor<ApiToken, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<ApiToken, int>, ApiTokenDbRepositoryQueryProvider>();

        //TransferEvent
        services.TryAddScoped<ITransferEventRepository, TransferEventRepository>();
        services.TryAddScoped<IRepository<TransferEvent, int>>(sp => sp.GetRequiredService<ITransferEventRepository>());
        services.TryAddTransient<IEntityKeyAccessor<TransferEvent, int>, DbEntityKeyAccessor<TransferEvent, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<TransferEvent, int>, TransferEventDbRepositoryQueryProvider>();

        //User
        services.TryAddScoped<IUserRepository, UserRepository>();
        services.TryAddScoped<IRepository<User, int>>(sp => sp.GetRequiredService<IUserRepository>());
        services.TryAddTransient<IEntityKeyAccessor<User, int>, DbEntityKeyAccessor<User, int>>();
        services.TryAddSingleton<IRepositoryQueryProvider<User, int>, UserDbRepositoryQueryProvider>();

        //SettingsItem
        services.TryAddScoped<ISettingsItemRepository, SettingsItemRepository>();
        services.TryAddScoped<IRepository<SettingsItem, string>>(sp => sp.GetRequiredService<ISettingsItemRepository>());
        services.TryAddTransient<IEntityKeyAccessor<SettingsItem, string>, DbEntityKeyAccessor<SettingsItem, string>>();
        services.TryAddSingleton<IRepositoryQueryProvider<SettingsItem, string>, SettingsItemDbRepositoryQueryProvider>();
        
        services.AddSingleton<IEntityValidator<object>, ValidatableObjectEntityValidator>();

        services.AddSingleton<ApplicationTimeService>();
        services.AddSingleton<ITimeService>(sp => sp.GetRequiredService<ApplicationTimeService>());
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton(new MemoryCacheServiceOptions { UseCacheDependenciesSupport = false });

        services.AddMemoryCache();
        
        return services;
    }
}