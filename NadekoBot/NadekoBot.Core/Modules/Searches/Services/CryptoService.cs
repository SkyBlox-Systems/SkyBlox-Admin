﻿using Discord;
using NadekoBot.Core.Modules.Searches.Common;
using NadekoBot.Core.Services;
using NadekoBot.Extensions;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NadekoBot.Core.Modules.Searches.Services
{
    public class CryptoService : INService
    {
        private readonly IDataCache _cache;
        private readonly IHttpClientFactory _httpFactory;
        private readonly Logger _log;

        public CryptoService(IDataCache cache, IHttpClientFactory httpFactory)
        {
            _cache = cache;
            _httpFactory = httpFactory;
            _log = NLog.LogManager.GetCurrentClassLogger();
        }

        public async Task<(CryptoResponseData Data, CryptoResponseData Nearest)> GetCryptoData(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return (null, null);
            }

            name = name.ToUpperInvariant();
            var cryptos = await CryptoData().ConfigureAwait(false);

            var crypto = cryptos
                ?.FirstOrDefault(x => x.Id.ToUpperInvariant() == name || x.Name.ToUpperInvariant() == name
                    || x.Symbol.ToUpperInvariant() == name);

            (CryptoResponseData Elem, int Distance)? nearest = null;
            if (crypto == null)
            {
                nearest = cryptos.Select(x => (x, Distance: x.Name.ToUpperInvariant().LevenshteinDistance(name)))
                    .OrderBy(x => x.Distance)
                    .Where(x => x.Distance <= 2)
                    .FirstOrDefault();

                crypto = nearest?.Elem;
            }

            if (nearest != null)
            {
                return (null, crypto);
            }

            return (crypto, null);
        }

        private readonly SemaphoreSlim getCryptoLock = new SemaphoreSlim(1, 1);
        public async Task<List<CryptoResponseData>> CryptoData()
        {
            await getCryptoLock.WaitAsync();
            try
            {
                var fullStrData = await _cache.GetOrAddCachedDataAsync("nadeko:crypto_data", async _ =>
                {
                    try
                    {
                        using (var _http = _httpFactory.CreateClient())
                        {
                            var strData = await _http.GetStringAsync(new Uri($"https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest?" +
                                $"CMC_PRO_API_KEY=e79ec505-0913-439d-ae07-069e296a6079" +
                                $"&start=1" +
                                $"&limit=500" +
                                $"&convert=USD"));

                            JsonConvert.DeserializeObject<CryptoResponse>(strData); // just to see if its' valid

                            return strData;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error getting crypto data: {Message}", ex.Message);
                        return default;
                    }

                }, "", TimeSpan.FromHours(1));

                return JsonConvert.DeserializeObject<CryptoResponse>(fullStrData).Data;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error retreiving crypto data: {Message}", ex.Message);
                return default;
            }
            finally
            {
                getCryptoLock.Release();
            }
        }
    }
}
