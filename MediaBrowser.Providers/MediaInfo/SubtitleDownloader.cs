﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Extensions;

namespace MediaBrowser.Providers.MediaInfo
{
    public class SubtitleDownloader
    {
        private readonly ILogger _logger;
        private readonly ISubtitleManager _subtitleManager;

        public SubtitleDownloader(ILogger logger, ISubtitleManager subtitleManager)
        {
            _logger = logger;
            _subtitleManager = subtitleManager;
        }

        public async Task<List<string>> DownloadSubtitles(Video video,
            List<MediaStream> mediaStreams,
            bool skipIfEmbeddedSubtitlesPresent,
            bool skipIfAudioTrackMatches,
            bool requirePerfectMatch,
            IEnumerable<string> languages,
            string[] disabledSubtitleFetchers,
            string[] subtitleFetcherOrder,
            CancellationToken cancellationToken)
        {
            var downloadedLanguages = new List<string>();

            foreach (var lang in languages)
            {
                var downloaded = await DownloadSubtitles(video, mediaStreams, skipIfEmbeddedSubtitlesPresent,
                    skipIfAudioTrackMatches, requirePerfectMatch, lang, disabledSubtitleFetchers, subtitleFetcherOrder, cancellationToken).ConfigureAwait(false);

                if (downloaded)
                {
                    downloadedLanguages.Add(lang);
                }
            }

            return downloadedLanguages;
        }

        public ValueTask<bool> DownloadSubtitles(Video video,
            List<MediaStream> mediaStreams,
            bool skipIfEmbeddedSubtitlesPresent,
            bool skipIfAudioTrackMatches,
            bool requirePerfectMatch,
            string lang,
            string[] disabledSubtitleFetchers,
            string[] subtitleFetcherOrder,
            CancellationToken cancellationToken)
        {
            if (video.VideoType != VideoType.VideoFile)
            {
                return new ValueTask<bool>(Task.FromResult(false));
            }

            if (!video.IsCompleteMedia)
            {
                return new ValueTask<bool>(Task.FromResult(false));
            }

            VideoContentType mediaType;

            if (video is Episode)
            {
                mediaType = VideoContentType.Episode;
            }
            else if (video is Movie)
            {
                mediaType = VideoContentType.Movie;
            }
            else
            {
                // These are the only supported types
                return new ValueTask<bool>(Task.FromResult(false));
            }

            return DownloadSubtitles(video, mediaStreams, skipIfEmbeddedSubtitlesPresent, skipIfAudioTrackMatches,
                requirePerfectMatch, lang, disabledSubtitleFetchers, subtitleFetcherOrder, mediaType, cancellationToken);
        }

        private async ValueTask<bool> DownloadSubtitles(Video video,
            List<MediaStream> mediaStreams,
            bool skipIfEmbeddedSubtitlesPresent,
            bool skipIfAudioTrackMatches,
            bool requirePerfectMatch,
            string language,
            string[] disabledSubtitleFetchers,
            string[] subtitleFetcherOrder,
            VideoContentType mediaType,
            CancellationToken cancellationToken)
        {
            // There's already subtitles for this language
            if (mediaStreams.Any(i => i.Type == MediaStreamType.Subtitle && i.IsTextSubtitleStream && string.Equals(i.Language, language, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var audioStreams = mediaStreams.Where(i => i.Type == MediaStreamType.Audio).ToList();
            var defaultAudioStreams = audioStreams.Where(i => i.IsDefault).ToList();

            // If none are marked as default, just take a guess
            if (defaultAudioStreams.Count == 0)
            {
                defaultAudioStreams = audioStreams.Take(1).ToList();
            }

            // There's already a default audio stream for this language
            if (skipIfAudioTrackMatches &&
                defaultAudioStreams.Any(i => string.Equals(i.Language, language, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // There's an internal subtitle stream for this language
            if (skipIfEmbeddedSubtitlesPresent &&
                mediaStreams.Any(i => i.Type == MediaStreamType.Subtitle && !i.IsExternal && string.Equals(i.Language, language, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var request = new SubtitleSearchRequest
            {
                ContentType = mediaType,
                IndexNumber = video.IndexNumber,
                Language = language,
                MediaPath = video.Path,
                Name = video.Name,
                ParentIndexNumber = video.ParentIndexNumber,
                ProductionYear = video.ProductionYear,
                ProviderIds = video.ProviderIds,

                // Stop as soon as we find something
                SearchAllProviders = false,

                IsPerfectMatch = requirePerfectMatch,
                DisabledSubtitleFetchers = disabledSubtitleFetchers,
                SubtitleFetcherOrder = subtitleFetcherOrder
            };

            var episode = video as Episode;

            if (episode != null)
            {
                request.IndexNumberEnd = episode.IndexNumberEnd;
                request.SeriesName = episode.SeriesName;
            }

            try
            {
                var searchResults = await _subtitleManager.SearchSubtitles(request, cancellationToken).ConfigureAwait(false);

                var result = searchResults.FirstOrDefault();

                if (result != null)
                {
                    await _subtitleManager.DownloadSubtitles(video, result.Id, cancellationToken).ConfigureAwait(false);

                    return true;
                }
            }
            catch (RateLimitExceededException)
            {
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error downloading subtitles", ex);
            }

            return false;
        }
    }
}
