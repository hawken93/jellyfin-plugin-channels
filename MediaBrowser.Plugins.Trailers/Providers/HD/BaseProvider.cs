﻿using HtmlAgilityPack;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Plugins.Trailers.Providers.HD
{
    public abstract class BaseProvider : GlobalBaseProvider
    {
        private readonly ILogger _logger;

        protected string BaseUrl = "http://www.hd-trailers.net/";

        protected BaseProvider(ILogger logger)
        {
            _logger = logger;
        }

        public abstract TrailerType TrailerType { get; }

        public async Task<IEnumerable<ChannelItemInfo>> GetChannelItems(string url, CancellationToken cancellationToken)
        {
            var list = new List<ChannelItemInfo>();

            var html = await EntryPoint.Instance.GetAndCacheResponse(url, TimeSpan.FromDays(7), cancellationToken)
                        .ConfigureAwait(false);


            // looking for HREF='/movie/automata'
            const string hrefPattern = "href=\"(?<url>.*?)\"";
            var matches = Regex.Matches(html, hrefPattern, RegexOptions.IgnoreCase);

            for (var i = 0; i < matches.Count; i++)
            {
                var trailerUrl = matches[i].Groups["url"].Value;

                if (!string.IsNullOrEmpty(trailerUrl) && trailerUrl.TrimStart('/').StartsWith("movie/", StringComparison.OrdinalIgnoreCase))
                {
                    trailerUrl = BaseUrl + trailerUrl.TrimStart('/');

                    try
                    {
                        var info = await GetTrailerFromUrl(trailerUrl, TrailerType, cancellationToken).ConfigureAwait(false);

                        if (info != null)
                        {
                            //list.Add(info);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error getting trailer info", ex);
                    }
                }
            }

            return list;
        }

        private async Task<ChannelItemInfo> GetTrailerFromUrl(string url, TrailerType type, CancellationToken cancellationToken)
        {
            var html = await EntryPoint.Instance.GetAndCacheResponse(url, TimeSpan.FromDays(14), cancellationToken)
                        .ConfigureAwait(false);

            var document = new HtmlDocument();
            document.LoadHtml(html);

            var titleElement = document.DocumentNode.SelectSingleNode("//h1");

            var links = document.DocumentNode.SelectNodes("//a");
            var linksList = links == null ? new List<HtmlNode>() : links.ToList();

            var info = new ChannelItemInfo
            {
                ContentType = ChannelMediaContentType.MovieExtra,
                ExtraType = ExtraType.Trailer,
                TrailerType = type,
                Id = url,
                MediaType = ChannelMediaType.Video,
                Type = ChannelItemType.Media,
                Name = titleElement == null ? null : titleElement.InnerText,
                MediaSources = GetMediaInfo(linksList, html)
            };

            // For older trailers just rely on core image providers
            if (TrailerType != TrailerType.ComingSoonToTheaters)
            {
                info.ImageUrl = null;
            }

            return info;
        }

        private List<ChannelMediaInfo> GetMediaInfo(IEnumerable<HtmlNode> nodes, string html)
        {
            var links = nodes.Select(i => i.GetAttributeValue("href", ""))
                .ToList();

            var list = new List<ChannelMediaInfo>();

            foreach (var link in links)
            {
                if (ValidContainers.Any(i => link.EndsWith(i, StringComparison.OrdinalIgnoreCase)))
                {
                    //_logger.Debug("Found url: " + link);

                    var url = link;

                    if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        url = BaseUrl + url.TrimStart('/');
                    }

                    list.Add(new ChannelMediaInfo
                    {
                        Container = Path.GetExtension(link).TrimStart('.'),
                        Path = url,
                        Protocol = MediaProtocol.Http,
                        RequiredHttpHeaders = GetRequiredHttpHeaders(url)
                    });
                }
            }

            return list
                .Where(i => ValidDomains.Any(d => i.Path.IndexOf(d, StringComparison.OrdinalIgnoreCase) != -1))
                .Select(SetValues)
                .ToList();
        }
    }
}
