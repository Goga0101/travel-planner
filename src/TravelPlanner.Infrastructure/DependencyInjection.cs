using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Infrastructure.Catalog;
using TravelPlanner.Infrastructure.ExchangeRates;
using TravelPlanner.Infrastructure.Jobs;
using TravelPlanner.Infrastructure.Persistence;
using TravelPlanner.Infrastructure.Providers;
using TravelPlanner.Infrastructure.Providers.Amadeus;
using TravelPlanner.Infrastructure.Providers.BookingCom;
using TravelPlanner.Infrastructure.Providers.Demo;
using TravelPlanner.Infrastructure.Providers.Geoapify;
using TravelPlanner.Infrastructure.Providers.Google;
using TravelPlanner.Infrastructure.Providers.Travelpayouts;
using TravelPlanner.Infrastructure.Providers.Ticketmaster;
using TravelPlanner.Infrastructure.Providers.Transitous;
using TravelPlanner.Infrastructure.Providers.Xotelo;

namespace TravelPlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TravelPlanner")
                               ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:TravelPlanner' is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddMemoryCache();
        services.Configure<PlanningOptions>(configuration.GetSection(PlanningOptions.SectionName));
        services.Configure<BudgetOptions>(configuration.GetSection(BudgetOptions.SectionName));
        services.Configure<OutboundLinkOptions>(configuration.GetSection(OutboundLinkOptions.SectionName));
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.SectionName));
        services.Configure<ExchangeRateOptions>(configuration.GetSection(ExchangeRateOptions.SectionName));
        services.Configure<AmadeusOptions>(configuration.GetSection(AmadeusOptions.SectionName));
        services.Configure<TravelpayoutsOptions>(configuration.GetSection(TravelpayoutsOptions.SectionName));
        services.Configure<BookingComOptions>(configuration.GetSection(BookingComOptions.SectionName));
        services.Configure<GoogleRoutesOptions>(configuration.GetSection(GoogleRoutesOptions.SectionName));
        services.Configure<BookingComLinkOptions>(configuration.GetSection(BookingComLinkOptions.SectionName));
        services.AddSingleton<IAccommodationSearchLinkBuilder, BookingComSearchLinkBuilder>();

        services.AddSingleton<CatalogFileReader>();
        services.AddSingleton<WorldAirportsFileReader>();
        services.AddSingleton<IWorldPlaces, WorldPlaces>();
        services.AddSingleton<ITransitGuideSource, TransitGuideFile>();
        // Departure preselection by IP. Short timeout: the search form falls back to its default when this is slow.
        services.AddHttpClient<IVisitorLocator, TravelpayoutsVisitorLocator>(c =>
        {
            c.BaseAddress = new Uri("https://www.travelpayouts.com/");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<NearbyPlacesFile>();
        services.AddTransient<INearbyPlacesSource, NearbyPlacesService>();
        services.AddScoped<CatalogSeeder>();

        services.AddSingleton<TripSearchQueue>();
        services.AddSingleton<ITripSearchQueue>(sp => sp.GetRequiredService<TripSearchQueue>());
        services.AddSingleton<ISearchCancellationRegistry, SearchCancellationRegistry>();
        services.AddSingleton<ISearchProgressReporter, SearchProgressReporter>();
        services.AddHostedService<TripSearchWorker>();

        var providers = configuration.GetSection(ProvidersOptions.SectionName).Get<ProvidersOptions>() ?? new ProvidersOptions();
        services.AddSingleton(new DataModeAccessor(providers.Mode));
        services.AddSingleton<IDataModeAccessor>(sp => sp.GetRequiredService<DataModeAccessor>());

        services.AddHttpClient<IExchangeRateProvider, EcbExchangeRateProvider>(c => c.Timeout = TimeSpan.FromSeconds(15))
            .AddStandardResilienceHandler(o => Configure(o, providers.Http));

        var status = new ProviderStatusRegistry();
        services.AddSingleton(status);

        if (providers.Mode == DataMode.Demo)
            RegisterDemoProviders(services, status);
        else
            RegisterLiveProviders(services, configuration, providers, status);

        // Nearby places are real OpenStreetMap data in every mode (no prices), so they are registered for Demo and Live alike.
        services.Configure<GeoapifyOptions>(configuration.GetSection(GeoapifyOptions.SectionName));
        var geoapify = configuration.GetSection(GeoapifyOptions.SectionName).Get<GeoapifyOptions>() ?? new GeoapifyOptions();
        if (geoapify.IsConfigured)
        {
            services.AddHttpClient<GeoapifyPlacesClient>(c => c.BaseAddress = BaseUri(geoapify.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddHostedService<NearbyCacheWarmer>();
            services.AddTransient<IAttractionSource, AttractionSource>();
            services.AddTransient<IActivityPlaceSource, ActivityPlaceSource>();
        }
        else
        {
            services.AddSingleton<IAttractionSource, NoAttractionSource>();
            services.AddSingleton<IActivityPlaceSource, NoActivityPlaceSource>();
        }
        // Wikimedia asks API clients to identify themselves with a descriptive User-Agent.
        services.AddHttpClient(AttractionSource.WikimediaClient, c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("TravelPlanner/1.0 (https://github.com/Goga0101/travel-planner)");
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddStandardResilienceHandler(o => Configure(o, providers.Http));
        status.Add(new ProviderStatus("Places", GeoapifyPlacesClient.ProviderName, geoapify.IsConfigured, false,
            geoapify.IsConfigured ? "status.geoapifyOn" : "status.geoapifyOff"));

        // Public-transport journeys from the airport to the stay (Transitous, free, fair use; identify with a User-Agent).
        services.Configure<TransitousOptions>(configuration.GetSection(TransitousOptions.SectionName));
        var transitous = configuration.GetSection(TransitousOptions.SectionName).Get<TransitousOptions>() ?? new TransitousOptions();
        services.AddHttpClient<ITransitRouter, TransitousRouter>(c =>
            {
                c.BaseAddress = BaseUri(transitous.BaseUrl);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("TravelPlanner/1.0 (https://github.com/Goga0101/travel-planner)");
            })
            .AddStandardResilienceHandler(o => Configure(o, providers.Http));
        status.Add(new ProviderStatus("Routes", TransitousRouter.ProviderName, transitous.Enabled, false,
            transitous.Enabled ? "status.transitousOn" : "status.transitousOff"));

        // Events for the day planner (real data in every mode; optional free key).
        services.Configure<TicketmasterOptions>(configuration.GetSection(TicketmasterOptions.SectionName));
        var ticketmaster = configuration.GetSection(TicketmasterOptions.SectionName).Get<TicketmasterOptions>() ?? new TicketmasterOptions();
        if (ticketmaster.IsConfigured)
            services.AddHttpClient<IEventSource, TicketmasterEventSource>(c => c.BaseAddress = BaseUri(ticketmaster.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
        else
            services.AddSingleton<IEventSource, NoEventSource>();
        status.Add(new ProviderStatus("Events", TicketmasterEventSource.ProviderName, ticketmaster.IsConfigured, false,
            ticketmaster.IsConfigured ? "status.ticketmasterOn" : "status.ticketmasterOff"));

        // Indicative hotel prices (never part of a plan total). Off in Demo mode, where everything must be sample data.
        services.Configure<XoteloOptions>(configuration.GetSection(XoteloOptions.SectionName));
        var xotelo = configuration.GetSection(XoteloOptions.SectionName).Get<XoteloOptions>() ?? new XoteloOptions();
        var xoteloActive = xotelo.Enabled && providers.Mode == DataMode.Live;
        if (xoteloActive)
            services.AddHttpClient<IHotelPriceSource, XoteloHotelPriceSource>(c => c.BaseAddress = BaseUri(xotelo.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
        else
            services.AddSingleton<IHotelPriceSource, NoHotelPriceSource>();
        status.Add(new ProviderStatus("Accommodation", XoteloHotelPriceSource.SourceName, xoteloActive, false,
            xoteloActive ? "status.xoteloOn" : "status.xoteloOff"));

        return services;
    }

    private static void RegisterDemoProviders(IServiceCollection services, ProviderStatusRegistry status)
    {
        services.AddSingleton<IFlightSearchProvider, DemoFlightSearchProvider>();
        services.AddSingleton<IAccommodationSearchProvider, DemoAccommodationProvider>();
        services.AddSingleton<ITransportSearchProvider, DemoTransportProvider>();
        foreach (var category in new[] { "Flights", "Accommodation", "Transfers" })
            status.Add(new ProviderStatus(category, "Demo data (not real)", true, true, "status.demo"));
    }

    private static void RegisterLiveProviders(IServiceCollection services, IConfiguration configuration, ProvidersOptions providers, ProviderStatusRegistry status)
    {
        var amadeus = configuration.GetSection(AmadeusOptions.SectionName).Get<AmadeusOptions>() ?? new AmadeusOptions();
        if (amadeus.IsConfigured)
        {
            services.AddSingleton<AmadeusTokenProvider>();
            services.AddHttpClient(AmadeusTokenProvider.HttpClientName, c => c.BaseAddress = BaseUri(amadeus.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddHttpClient<AmadeusFlightSearchProvider>(c => c.BaseAddress = BaseUri(amadeus.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddTransient<IFlightSearchProvider>(sp => new CachingFlightSearchProvider(
                sp.GetRequiredService<AmadeusFlightSearchProvider>(), sp.GetRequiredService<IMemoryCache>(), TimeSpan.FromMinutes(providers.FlightCacheMinutes)));
        }
        status.Add(new ProviderStatus("Flights", "Amadeus", amadeus.IsConfigured, false,
            amadeus.IsConfigured ? "status.amadeusOn" : "status.amadeusOff"));

        var travelpayouts = configuration.GetSection(TravelpayoutsOptions.SectionName).Get<TravelpayoutsOptions>() ?? new TravelpayoutsOptions();
        if (travelpayouts.IsConfigured)
        {
            services.AddHttpClient<TravelpayoutsFlightProvider>(c => c.BaseAddress = BaseUri(travelpayouts.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddTransient<IFlightSearchProvider>(sp => new CachingFlightWindowSearchProvider(
                sp.GetRequiredService<TravelpayoutsFlightProvider>(), sp.GetRequiredService<IMemoryCache>(), TimeSpan.FromMinutes(providers.FlightCacheMinutes)));
        }
        status.Add(new ProviderStatus("Flights", TravelpayoutsFlightOfferMapper.ProviderName, travelpayouts.IsConfigured, false,
            travelpayouts.IsConfigured ? "status.travelpayoutsOn" : "status.travelpayoutsOff"));

        var booking = configuration.GetSection(BookingComOptions.SectionName).Get<BookingComOptions>() ?? new BookingComOptions();
        if (booking.IsConfigured)
        {
            services.AddHttpClient<BookingComAccommodationProvider>(c => c.BaseAddress = BaseUri(booking.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddTransient<IAccommodationSearchProvider>(sp => new CachingAccommodationProvider(
                sp.GetRequiredService<BookingComAccommodationProvider>(), sp.GetRequiredService<IMemoryCache>(), TimeSpan.FromMinutes(providers.AccommodationCacheMinutes)));
        }
        status.Add(new ProviderStatus("Accommodation", BookingComMapper.ProviderName, booking.IsConfigured, false,
            booking.IsConfigured ? "status.bookingOn" : "status.bookingOff"));

        var google = configuration.GetSection(GoogleRoutesOptions.SectionName).Get<GoogleRoutesOptions>() ?? new GoogleRoutesOptions();
        if (google.IsConfigured)
        {
            services.AddHttpClient<GoogleRoutesTransportProvider>(c => c.BaseAddress = BaseUri(google.BaseUrl))
                .AddStandardResilienceHandler(o => Configure(o, providers.Http));
            services.AddTransient<ITransportSearchProvider>(sp => new CachingTransportProvider(
                sp.GetRequiredService<GoogleRoutesTransportProvider>(), sp.GetRequiredService<IMemoryCache>(), TimeSpan.FromMinutes(providers.TransportCacheMinutes)));
        }
        status.Add(new ProviderStatus("Transfers", GoogleRoutesMapper.ProviderName, google.IsConfigured, false,
            google.IsConfigured ? "status.googleOn" : "status.googleOff"));
    }

    private static Uri BaseUri(string url) => new(url.TrimEnd('/') + "/");

    private static void Configure(HttpStandardResilienceOptions o, HttpResilienceSettings settings)
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds));
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(settings.AttemptTimeoutSeconds, settings.TotalTimeoutSeconds));
        o.Retry.MaxRetryAttempts = Math.Max(1, settings.MaxRetryAttempts);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, settings.AttemptTimeoutSeconds * 2 + 1));
    }
}
