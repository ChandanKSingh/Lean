﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Newtonsoft.Json;
using QuantConnect.Data.Custom.Quiver;
using QuantConnect.Logging;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuantConnect.ToolBox.QuiverDataDownloader
{
    /// <summary>
    /// Quiver downloader implementation for <see cref="QuiverWikipedia"/> data type
    /// </summary>
    public class QuiverWikipediaDataDownloader : QuiverDataDownloader
    {
        private readonly string _destinationFolder;


        /// <summary>
        /// Creates a new instance of <see cref="QuiverWikipediaDataDownloader"/>
        /// </summary>
        /// <param name="destinationFolder">The folder where the data will be saved</param>
        public QuiverWikipediaDataDownloader(string destinationFolder, string apiKey = null) : base(apiKey)
        {
            _destinationFolder = Path.Combine(destinationFolder, "wikipedia");


            Directory.CreateDirectory(_destinationFolder);
        }

        /// <summary>
        /// Runs the instance of the object.
        /// </summary>
        /// <returns>True if process all downloads successfully</returns>
        public override bool Run()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var companies = GetCompanies().Result.DistinctBy(x => x.Ticker).ToList();
                var count = companies.Count;
                var currentPercent = 0.05;
                var percent = 0.05;
                var i = 0;

                Log.Trace($"QuiverWikipediaDataDownloader.Run(): Start processing {count.ToStringInvariant()} companies");

                var tasks = new List<Task>();

                foreach (var company in companies)
                {
                    // Include tickers that are "defunct".
                    // Remove the tag because it cannot be part of the API endpoint.
                    // This is separate from the NormalizeTicker(...) method since
                    // we don't convert tickers with `-`s into the format we can successfully
                    // index mapfiles with.
                    var quiverTicker = company.Ticker;
                    string ticker;


                    if (!TryNormalizeDefunctTicker(quiverTicker, out ticker))
                    {
                        Log.Error($"QuiverWikipediaDataDownloader(): Defunct ticker {quiverTicker} is unable to be parsed. Continuing...");
                        continue;
                    }

                    // Begin processing ticker with a normalized value
                    Log.Trace($"QuiverWikipediaDataDownloader.Run(): Processing {ticker}");

                    // Makes sure we don't overrun Quiver rate limits accidentally
                    IndexGate.WaitToProceed();

                    tasks.Add(
                        HttpRequester($"historical/wikipedia/{ticker}")
                            .ContinueWith(
                                y =>
                                {
                                    i++;

                                    if (y.IsFaulted)
                                    {
                                        Log.Error($"QuiverWikipediaDataDownloader.Run(): Failed to get data for {company}");
                                        return;
                                    }

                                    var result = y.Result;
                                    if (string.IsNullOrEmpty(result))
                                    {
                                        // We've already logged inside HttpRequester
                                        return;
                                    }

                                    var wikipediaPageViews = JsonConvert.DeserializeObject<List<QuiverWikipedia>>(result, JsonSerializerSettings);
                                    var csvContents = new List<string>();

                                    foreach (var wikipediaPage in wikipediaPageViews)
                                    {
                                        csvContents.Add(string.Join(",",
                                            $"{wikipediaPage.Date:yyyyMMdd}",
                                            $"{wikipediaPage.PageViews}",
                                            $"{wikipediaPage.WeekPercentChange}",
                                            $"{wikipediaPage.MonthPercentChange}"));
                                    }

                                    if (csvContents.Count != 0)
                                    {
                                        SaveContentToFile(_destinationFolder, ticker, csvContents);
                                    }

                                    var percentageDone = i / count;
                                    if (percentageDone >= currentPercent)
                                    {
                                        Log.Trace($"QuiverWikipediaDataDownloader.Run(): {percentageDone.ToStringInvariant("P2")} complete");
                                        currentPercent += percent;
                                    }
                                }
                            )
                    );
                    
                    if (tasks.Count == 10)
                    {
                        Task.WaitAll(tasks.ToArray());
                        tasks.Clear();
                    }
                }

                if (tasks.Count != 0)
                {
                    Task.WaitAll(tasks.ToArray());
                    tasks.Clear();
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                return false;
            }

            Log.Trace($"QuiverWikipediaDataDownloader.Run(): Finished in {stopwatch.Elapsed.ToStringInvariant(null)}");
            return true;
        }
    }
}
