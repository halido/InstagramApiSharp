﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Android.DeviceInfo;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.ResponseWrappers;
using InstagramApiSharp.Converters;
using InstagramApiSharp.Converters.Json;
using InstagramApiSharp.Enums;
using InstagramApiSharp.Helpers;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InstagramApiSharp.API.Processors
{
    /// <summary>
    ///     Story api functions.
    /// </summary>
    internal class StoryProcessor : IStoryProcessor
    {
        private readonly AndroidDevice _deviceInfo;
        private readonly HttpHelper _httpHelper;
        private readonly IHttpRequestProcessor _httpRequestProcessor;
        private readonly InstaApi _instaApi;
        private readonly IInstaLogger _logger;
        private readonly UserSessionData _user;
        private readonly UserAuthValidate _userAuthValidate;
        public StoryProcessor(AndroidDevice deviceInfo, UserSessionData user,
            IHttpRequestProcessor httpRequestProcessor, IInstaLogger logger,
            UserAuthValidate userAuthValidate, InstaApi instaApi, HttpHelper httpHelper)
        {
            _deviceInfo = deviceInfo;
            _user = user;
            _httpRequestProcessor = httpRequestProcessor;
            _logger = logger;
            _userAuthValidate = userAuthValidate;
            _instaApi = instaApi;
            _httpHelper = httpHelper;
        }
        /// <summary>
        ///     Append to existing highlight
        /// </summary>
        /// <param name="highlightId">Highlight id</param>
        /// <param name="mediaId">Media id (CoverMedia.MediaId)</param>
        public async Task<IResult<bool>> AppendToHighlightFeedAsync(string highlightId, string mediaId)
        {
            return await AppendOrDeleteHighlight(highlightId, mediaId, false);
        }

        /// <summary>
        ///     Create new highlight
        /// </summary>
        /// <param name="mediaId">Story media id</param>
        /// <param name="title">Highlight title</param>
        public async Task<IResult<InstaHighlightFeed>> CreateHighlightFeedAsync(string mediaId, string title)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var cover = new JObject
                {
                    {"media_id", mediaId},
                    {"crop_rect", new JArray { 0.0, 0.20198676, 1.0, 0.79801327 }.ToString(Formatting.None) }
                }.ToString(Formatting.None);
                var data = new JObject
                {
                    {"source", "self_profile"},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"cover", cover},
                    {"title", title},
                    {"media_ids", $"[{ExtensionHelper.EncodeList(new[] { mediaId })}]"}
                };

                var instaUri = UriCreator.GetHighlightCreateUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<InstaHighlightFeed>(response, json);

                var highlightFeedResponse = JsonConvert.DeserializeObject<InstaHighlightReelResponse>(json,
                    new InstaHighlightReelDataConverter());
                var highlightStoryFeed = ConvertersFabric.Instance.GetHighlightReelConverter(highlightFeedResponse).Convert();
                return Result.Success(highlightStoryFeed);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaHighlightFeed>(exception);
            }
        }

        /// <summary>
        ///     Delete highlight feed
        /// </summary>
        /// <param name="highlightId">Highlight id</param>
        /// <param name="mediaId">Media id (CoverMedia.MediaId)</param>
        public async Task<IResult<bool>> DeleteHighlightFeedAsync(string highlightId, string mediaId)
        {
            return await AppendOrDeleteHighlight(highlightId, mediaId, true);
        }

        /// <summary>
        ///     Delete a media story (photo or video)
        /// </summary>
        /// <param name="storyMediaId">Story media id</param>
        /// <param name="sharingType">The type of the media</param>
        /// <returns>Return true if the story media is deleted</returns>
        public async Task<IResult<bool>> DeleteStoryAsync(string storyMediaId, InstaSharingType sharingType = InstaSharingType.Video)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var deleteMediaUri = UriCreator.GetDeleteStoryMediaUri(storyMediaId, sharingType);

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"media_id", storyMediaId}
                };

                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, deleteMediaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);

                var deletedResponse = JsonConvert.DeserializeObject<DeleteResponse>(json);
                return Result.Success(deletedResponse.IsDeleted);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        
        /// <summary>
        ///     Get list of users that blocked from seeing your stories
        /// </summary>
        public async Task<IResult<InstaUserShortList>> GetBlockedUsersFromStoriesAsync()
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var list = new InstaUserShortList();
            try
            {
                var instaUri = UriCreator.GetBlockedStoriesUri();
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_csrftoken", _user.CsrfToken},
                };
                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaUserShortList>(response, obj.Message, null);

                var usersResponse = JsonConvert.DeserializeObject<InstaUserListShortResponse>(json);
                list.AddRange(
                    usersResponse.Items.Select(ConvertersFabric.Instance.GetUserShortConverter)
                        .Select(converter => converter.Convert()));
                return Result.Success(list);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, list);
            }
        }

        /// <summary>
        ///     Get user highlight feeds by user id (pk)
        /// </summary>
        /// <param name="userId">User id (pk)</param>
        public async Task<IResult<InstaHighlightFeeds>> GetHighlightFeedsAsync(long userId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetHighlightFeedsUri(userId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<InstaHighlightFeeds>(response, json);
                var highlightFeedResponse = JsonConvert.DeserializeObject<InstaHighlightFeedsResponse>(json);
                var highlightStoryFeed = ConvertersFabric.Instance.GetHighlightFeedsConverter(highlightFeedResponse).Convert();
                return Result.Success(highlightStoryFeed);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaHighlightFeeds>(exception);
            }
        }

        /// <summary>
        ///     Get user highlights archive
        ///     <para>Note: Use <see cref="IStoryProcessor.GetHighlightsArchiveMediasAsync(string)"/> to get hightlight medias of an specific day.</para>
        /// </summary>
        public async Task<IResult<InstaHighlightShortList>> GetHighlightsArchiveAsync()
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetHighlightsArchiveUri();
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<InstaHighlightShortList>(response, json);

                var obj = JsonConvert.DeserializeObject<InstaHighlightShortListResponse>(json);
                return Result.Success(ConvertersFabric.Instance.GetHighlightShortListConverter(obj).Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaHighlightShortList>(exception);
            }
        }

        /// <summary>
        ///     Get highlights archive medias
        ///     <para>Note: get highlight id from <see cref="IStoryProcessor.GetHighlightsArchiveAsync"/></para>
        /// </summary>
        /// <param name="highlightId">Highlight id (Get it from <see cref="IStoryProcessor.GetHighlightsArchiveAsync"/>)</param>
        public async Task<IResult<InstaHighlightSingleFeed>> GetHighlightsArchiveMediasAsync(string highlightId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                if (string.IsNullOrEmpty(highlightId))
                    throw new ArgumentNullException("highlightId cannot be null or empty");

                var instaUri = UriCreator.GetReelMediaUri();

                var data = new JObject
                {
                    {InstaApiConstants.SUPPORTED_CAPABALITIES_HEADER, InstaApiConstants.SupportedCapabalities.ToString(Formatting.None)},
                    {"source", "reel_highlights_gallery"},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"user_ids", new JArray(highlightId)}
                };

                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<InstaHighlightSingleFeed>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaHighlightReelResponse>(json,
                    new InstaHighlightReelsListDataConverter());

                return Result.Success(ConvertersFabric.Instance.GetHighlightReelConverter(obj).Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaHighlightSingleFeed>(exception);
            }
        }

        /// <summary>
        ///     Get single highlight medias
        ///     <para>Note: get highlight id from <see cref="IStoryProcessor.GetHighlightFeedsAsync(long)"/></para>
        /// </summary>
        /// <param name="highlightId">Highlight id (Get it from <see cref="IStoryProcessor.GetHighlightFeedsAsync(long)"/>)</param>
        public async Task<IResult<InstaHighlightSingleFeed>> GetHighlightMediasAsync(string highlightId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                if (string.IsNullOrEmpty(highlightId))
                    throw new ArgumentNullException("highlightId cannot be null or empty");

                var instaUri = UriCreator.GetReelMediaUri();
                var data = new JObject
                {
                    {InstaApiConstants.SUPPORTED_CAPABALITIES_HEADER, InstaApiConstants.SupportedCapabalities.ToString(Formatting.None)},
                    {"source", "profile"},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"user_ids", new JArray(highlightId)}
                };

                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<InstaHighlightSingleFeed>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaHighlightReelResponse>(json,
                    new InstaHighlightReelsListDataConverter());

                return Result.Success(ConvertersFabric.Instance.GetHighlightReelConverter(obj).Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaHighlightSingleFeed>(exception);
            }
        }

        /// <summary>
        ///     Get user story feed (stories from users followed by current user).
        /// </summary>
        public async Task<IResult<InstaStoryFeed>> GetStoryFeedAsync()
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            { 
                var storyFeedUri = UriCreator.GetStoryFeedUri();
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, storyFeedUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK) return Result.Fail("", (InstaStoryFeed) null);
                var storyFeedResponse = JsonConvert.DeserializeObject<InstaStoryFeedResponse>(json);
                var instaStoryFeed = ConvertersFabric.Instance.GetStoryFeedConverter(storyFeedResponse).Convert();
                return Result.Success(instaStoryFeed);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaStoryFeed>(exception);
            }
        }
        /// <summary>
        ///     Get story media viewers
        /// </summary>
        /// <param name="storyMediaId">Story media id</param>
        /// <param name="paginationParameters">Pagination parameters</param>
        public async Task<IResult<InstaReelStoryMediaViewers>> GetStoryMediaViewersAsync(string storyMediaId, PaginationParameters paginationParameters)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                if (paginationParameters.MaximumPagesToLoad > 1)
                    throw new Exception("Not supported");
                var directInboxUri = UriCreator.GetStoryMediaViewersUri(storyMediaId, paginationParameters?.NextMaxId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, directInboxUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaReelStoryMediaViewers>(response, json);
                var threadResponse = JsonConvert.DeserializeObject<InstaReelStoryMediaViewers>(json);

                return Result.Success(threadResponse);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaReelStoryMediaViewers>(exception);
            }
        }

        /// <summary>
        ///     Get the story by userId
        /// </summary>
        /// <param name="userId">User Id</param>
        public async Task<IResult<InstaStory>> GetUserStoryAsync(long userId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var userStoryUri = UriCreator.GetUserStoryUri(userId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, userStoryUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK) Result.UnExpectedResponse<InstaStory>(response, json);
                var userStoryResponse = JsonConvert.DeserializeObject<InstaStoryResponse>(json);
                var userStory = ConvertersFabric.Instance.GetStoryConverter(userStoryResponse).Convert();
                return Result.Success(userStory);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaStory>(exception);
            }
        }
        /// <summary>
        ///     Get user story reel feed. Contains user info last story including all story items.
        /// </summary>
        /// <param name="userId">User identifier (PK)</param>
        public async Task<IResult<InstaReelFeed>> GetUserStoryFeedAsync(long userId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var feed = new InstaReelFeed();
            try
            {
                var userFeedUri = UriCreator.GetUserReelFeedUri(userId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, userFeedUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaReelFeed>(response, json);
                var feedResponse = JsonConvert.DeserializeObject<InstaReelFeedResponse>(json);
                feed = ConvertersFabric.Instance.GetReelFeedConverter(feedResponse).Convert();
                return Result.Success(feed);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail(exception, feed);
            }
        }

        /// <summary>
        ///     Seen story
        /// </summary>
        /// <param name="storyMediaId">Story media identifier</param>
        /// <param name="takenAtUnix">Taken at unix</param>
        public async Task<IResult<bool>> MarkStoryAsSeenAsync(string storyMediaId, long takenAtUnix)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetSeenMediaStoryUri();
                var storyId = $"{storyMediaId}_{storyMediaId.Split('_')[1]}";
                var dateTimeUnix = DateTime.UtcNow.ToUnixTime();
                var reel = new JObject
                {
                    { storyId, new JArray($"{takenAtUnix}_{dateTimeUnix}") }
                };
                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"container_module", "feed_timeline"},
                    {"live_vods_skipped", new JObject()},
                    {"nuxes_skipped", new JObject()},
                    {"nuxes", new JObject()},
                    {"reels", reel},
                    {"live_vods", new JObject()},
                    {"reel_media_skipped", new JObject()}
                };
                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);
                return obj.Status.ToLower() == "ok" ? Result.Success(true) : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Seen highlight
        ///     <para>Get media id from <see cref="InstaHighlightFeed.CoverMedia.MediaId"/></para>
        /// </summary>
        /// <param name="mediaId">Media identifier (get it from <see cref="InstaHighlightFeed.CoverMedia.MediaId"/>)</param>
        /// <param name="highlightId">Highlight id</param>
        /// <param name="takenAtUnix">Taken at unix</param>
        public async Task<IResult<bool>> MarkHighlightAsSeenAsync(string mediaId, string highlightId, long takenAtUnix)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetSeenMediaStoryUri();
                var reelId = $"{mediaId}_{highlightId}";
                var dateTimeUnix = DateTime.UtcNow.ToUnixTime();

                var reel = new JObject
                {
                    { reelId, new JArray($"{takenAtUnix}_{dateTimeUnix}") }
                };
                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"container_module", "profile"},
                    {"live_vods_skipped", new JObject()},
                    {"nuxes_skipped", new JObject()},
                    {"nuxes", new JObject()},
                    {"reels", reel},
                    {"live_vods", new JObject()},
                    {"reel_media_skipped", new JObject()}
                };
                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);
                return obj.Status.ToLower() == "ok" ? Result.Success(true) : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Share story to someone
        /// </summary>
        /// <param name="reelId">Reel id</param>
        /// <param name="storyMediaId">Story media id</param>
        /// <param name="threadId">Thread id</param>
        /// <param name="text">Text to send (optional</param>
        /// <param name="sharingType">Sharing type</param>
        public async Task<IResult<InstaSharing>> ShareStoryAsync(string reelId, string storyMediaId, string threadId, string text, InstaSharingType sharingType = InstaSharingType.Video)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetStoryShareUri(sharingType.ToString().ToLower());
                var data = new JObject
                {
                    {"action", "send_item"},
                    {"thread_ids", $"[{threadId}]"},
                    {"unified_broadcast_format", "1"},
                    {"reel_id", reelId},
                    {"text", text ?? ""},
                    {"story_media_id", storyMediaId},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Add("Host", "i.instagram.com");
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaSharing>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaSharing>(json);

                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaSharing>(exception);
            }
        }

        /// <summary>
        ///     Reply to story
        ///     <para>Note: Get story media id from <see cref="InstaMedia.InstaIdentifier"/></para>
        /// </summary>
        /// <param name="storyMediaId">Media id (get it from <see cref="InstaMedia.InstaIdentifier"/>)</param>
        /// <param name="userId">Story owner user pk (get it from <see cref="InstaMedia.User.Pk"/>)</param>
        /// <param name="text">Text to send</param>
        public async Task<IResult<bool>> ReplyToStoryAsync(string storyMediaId, long userId, string text)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetBroadcastReelShareUri();
                var clientContext = Guid.NewGuid().ToString();
                var data = new Dictionary<string, string>
                {
                    {"recipient_users", $"[[{userId}]]"},
                    {"action", "send_item"},
                    {"client_context", clientContext},
                    {"media_id", storyMediaId},
                    {"_csrftoken", _user.CsrfToken},
                    {"text", text ?? string.Empty},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()}
                };

                var request =
                    _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);
                return obj.Status.ToLower() == "ok" ? Result.Success(true) : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Upload story photo
        /// </summary>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryPhotoAsync(InstaImage image, string caption)
        {
            return await UploadStoryPhotoAsync(null, image, caption);
        }
        /// <summary>
        ///     Upload story photo with progress
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryPhotoAsync(Action<InstaUploaderProgress> progress, InstaImage image, string caption)
        {
            return await UploadStoryPhotoWithUrlAsync(progress, image, caption, null);
        }
        /// <summary>
        ///     Upload story photo with adding link address
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryPhotoWithUrlAsync(InstaImage image, string caption, Uri uri)
        {
            return await UploadStoryPhotoWithUrlAsync(null, image, caption, uri);
        }
        /// <summary>
        ///     Upload story photo with adding link address (with progress)
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryPhotoWithUrlAsync(Action<InstaUploaderProgress> progress, InstaImage image, 
            string caption, Uri uri)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var upProgress = new InstaUploaderProgress
            {
                Caption = caption ?? string.Empty,
                UploadState = InstaUploadState.Preparing
            };
            try
            {
                var instaUri = UriCreator.GetUploadPhotoUri();
                var uploadId = ApiRequestMessage.GenerateUploadId();
                upProgress.UploadId = uploadId;
                progress?.Invoke(upProgress);
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"95\"}"),
                        "\"image_compression\""
                    }
                };
                byte[] imageBytes;
                if (image.ImageBytes == null)
                    imageBytes = File.ReadAllBytes(image.Uri);
                else
                    imageBytes = image.ImageBytes;
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                imageContent.Headers.Add("Content-Type", "application/octet-stream");

                //var progressContent = new ProgressableStreamContent(imageContent, 4096, progress)
                //{
                //    UploaderProgress = upProgress
                //};
                upProgress.UploadState = InstaUploadState.Uploading;
                progress?.Invoke(upProgress);
                requestContent.Add(imageContent, "photo", $"pending_media_{ApiRequestMessage.GenerateUploadId()}.jpg");
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    upProgress.UploadState = InstaUploadState.Uploaded;
                    progress?.Invoke(upProgress);
                    //upProgress = progressContent?.UploaderProgress;
                    return await ConfigureStoryPhotoAsync(progress, upProgress, image, uploadId, caption, uri);
                }
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaStoryMedia>(exception);
            }
        }
        /// <summary>
        ///     Upload story video (to self story)
        /// </summary>
        /// <param name="video">Video to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryVideoAsync(InstaVideoUpload video, string caption)
        {
            return await UploadStoryVideoAsync(null, video, caption);
        }

        /// <summary>
        ///     Upload story video (to self story) with progress
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryVideoAsync(Action<InstaUploaderProgress> progress, InstaVideoUpload video, string caption)
        {
            return await UploadStoryVideoWithUrlAsync(progress, video, caption, null);
        }

        /// <summary>
        ///     Upload story video (to self story)
        /// </summary>
        /// <param name="video">Video to upload</param>
        public async Task<IResult<bool>> UploadStoryVideoAsync(InstaVideoUpload video,
    InstaStoryType storyType = InstaStoryType.SelfStory, params string[] threadIds)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            return await _instaApi.HelperProcessor.SendVideoAsync(null, false, false, "", InstaViewMode.Replayable, storyType, null, threadIds.EncodeList(), video);
        }

        /// <summary>
        ///     Upload story video (to self story) with progress
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video to upload</param>
        public async Task<IResult<bool>> UploadStoryVideoAsync(Action<InstaUploaderProgress> progress, InstaVideoUpload video,
    InstaStoryType storyType = InstaStoryType.SelfStory, params string[] threadIds)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            return await _instaApi.HelperProcessor.SendVideoAsync(progress, false, false, "", InstaViewMode.Replayable, storyType, null, threadIds.EncodeList(), video);
        }

        /// <summary>
        ///     Upload story video (to self story) with adding link address
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryVideoWithUrlAsync(InstaVideoUpload video, string caption, Uri uri)
        {
            return await UploadStoryVideoWithUrlAsync(null, video, caption, uri);
        }

        /// <summary>
        ///     Upload story video (to self story) with adding link address (with progress)
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        public async Task<IResult<InstaStoryMedia>> UploadStoryVideoWithUrlAsync(Action<InstaUploaderProgress> progress,
            InstaVideoUpload video, string caption, Uri uri)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var upProgress = new InstaUploaderProgress
            {
                Caption = caption ?? string.Empty,
                UploadState = InstaUploadState.Preparing
            };
            try
            {
                var uploadId = ApiRequestMessage.GenerateRandomUploadId();
                var videoHashCode = Path.GetFileName(video.Video.Uri ?? $"C:\\{13.GenerateRandomString()}.mp4").GetHashCode();
                var photoHashCode = Path.GetFileName(video.VideoThumbnail.Uri ?? $"C:\\{13.GenerateRandomString()}.jpg").GetHashCode();

                var waterfallId = Guid.NewGuid().ToString();

                var videoEntityName = $"{uploadId}_0_{videoHashCode}";
                var videoUri = UriCreator.GetStoryUploadVideoUri(uploadId, videoHashCode);

                var photoEntityName = $"{uploadId}_0_{photoHashCode}";
                var photoUri = UriCreator.GetStoryUploadPhotoUri(uploadId, photoHashCode);

                upProgress.UploadId = uploadId;
                progress?.Invoke(upProgress);
                var videoMediaInfoData = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"media_info", new JObject
                        {
                              {"capture_mode", "normal"},
                              {"media_type", 2},
                              {"caption", caption},
                              {"mentions", new JArray()},
                              {"hashtags", new JArray()},
                              {"locations", new JArray()},
                              {"stickers", new JArray()},
                        }
                    }
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, UriCreator.GetStoryMediaInfoUploadUri(), _deviceInfo, videoMediaInfoData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                var videoUploadParamsObj = new JObject
                {
                    {"upload_media_height", "0"},
                    {"upload_media_width", "0"},
                    {"upload_media_duration_ms", "46000"},
                    {"upload_id", uploadId},
                    {"for_album", "1"},
                    {"retry_context", "{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"},
                    {"media_type", "2"},
                };
                var videoUploadParams = JsonConvert.SerializeObject(videoUploadParamsObj);
                request = _httpHelper.GetDefaultRequest(HttpMethod.Get, videoUri, _deviceInfo);
                request.Headers.Add("X_FB_VIDEO_WATERFALL_ID", waterfallId);
                request.Headers.Add("X-Instagram-Rupload-Params", videoUploadParams);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
                }


                var videoBytes = video.Video.VideoBytes ?? File.ReadAllBytes(video.Video.Uri);
                var videoContent = new ByteArrayContent(videoBytes);
                videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
                videoContent.Headers.Add("Content-Type", "application/octet-stream");
                //var progressContent = new ProgressableStreamContent(videoContent, 4096, progress)
                //{
                //    UploaderProgress = upProgress
                //};
                request = _httpHelper.GetDefaultRequest(HttpMethod.Post, videoUri, _deviceInfo);
                request.Content = videoContent;
                upProgress.UploadState = InstaUploadState.Uploading;
                progress?.Invoke(upProgress);
                var vidExt = Path.GetExtension(video.Video.Uri ?? $"C:\\{13.GenerateRandomString()}.mp4").Replace(".", "").ToLower();
                if (vidExt == "mov")
                    request.Headers.Add("X-Entity-Type", "video/quicktime");
                else
                    request.Headers.Add("X-Entity-Type", "video/mp4");
                request.Headers.Add("Offset", "0");
                request.Headers.Add("X-Instagram-Rupload-Params", videoUploadParams);
                request.Headers.Add("X-Entity-Name", videoEntityName);
                request.Headers.Add("X-Entity-Length", videoBytes.Length.ToString());
                request.Headers.Add("X_FB_VIDEO_WATERFALL_ID", waterfallId);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
                }
                upProgress.UploadState = InstaUploadState.Uploaded;
                progress?.Invoke(upProgress);
                var photoUploadParamsObj = new JObject
                {
                    {"retry_context", "{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"},
                    {"media_type", "2"},
                    {"upload_id", uploadId},
                    {"image_compression", "{\"lib_name\":\"moz\",\"lib_version\":\"3.1.m\",\"quality\":\"95\"}"},
                };
                var photoUploadParams = JsonConvert.SerializeObject(photoUploadParamsObj);
                request = _httpHelper.GetDefaultRequest(HttpMethod.Get, photoUri, _deviceInfo);
                request.Headers.Add("X_FB_PHOTO_WATERFALL_ID", waterfallId);
                request.Headers.Add("X-Instagram-Rupload-Params", photoUploadParams);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
                }

                upProgress.UploadState = InstaUploadState.UploadingThumbnail;
                progress?.Invoke(upProgress);
                var imageBytes = video.VideoThumbnail.ImageBytes ?? File.ReadAllBytes(video.VideoThumbnail.Uri);
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                imageContent.Headers.Add("Content-Type", "application/octet-stream");
                request = _httpHelper.GetDefaultRequest(HttpMethod.Post, photoUri, _deviceInfo);
                request.Content = imageContent;
                request.Headers.Add("X-Entity-Type", "image/jpeg");
                request.Headers.Add("Offset", "0");
                request.Headers.Add("X-Instagram-Rupload-Params", photoUploadParams);
                request.Headers.Add("X-Entity-Name", photoEntityName);
                request.Headers.Add("X-Entity-Length", imageBytes.Length.ToString());
                request.Headers.Add("X_FB_PHOTO_WATERFALL_ID", waterfallId);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    //upProgress = progressContent?.UploaderProgress;
                    upProgress.UploadState = InstaUploadState.ThumbnailUploaded;
                    progress?.Invoke(upProgress);
                    return await ConfigureStoryVideoAsync(progress, upProgress, video, uploadId, caption, uri);
                }
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaStoryMedia>(exception);
            }
        }

        /// <summary>
        ///     Upload story video [to self story, to direct threads or both(self and direct)] with adding link address
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="video">Video to upload</param>
        /// <param name="uri">Uri to add</param>
        /// <param name="storyType">Story type</param>
        /// <param name="threadIds">Thread ids</param>
        public async Task<IResult<bool>> UploadStoryVideoWithUrlAsync(InstaVideoUpload video, Uri uri,
    InstaStoryType storyType = InstaStoryType.SelfStory, params string[] threadIds)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            return await _instaApi.HelperProcessor.SendVideoAsync(null, false, false, "", InstaViewMode.Replayable, storyType, null, threadIds.EncodeList(), video, uri);
        }

        /// <summary>
        ///     Upload story video (to self story) with adding link address (with progress)
        ///     <para>Note: this function only works with verified account or you have more than 10k followers.</para>
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video to upload</param>
        /// <param name="storyType">Story type</param>
        /// <param name="threadIds">Thread ids</param>
        public async Task<IResult<bool>> UploadStoryVideoWithUrlAsync(Action<InstaUploaderProgress> progress, InstaVideoUpload video, Uri uri,
InstaStoryType storyType = InstaStoryType.SelfStory, params string[] threadIds)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            return await _instaApi.HelperProcessor.SendVideoAsync(progress, false, false, "", InstaViewMode.Replayable, storyType, null, threadIds.EncodeList(), video, uri);
        }

        /// <summary>
        ///     Validate url for adding to story link
        /// </summary>
        /// <param name="url">Url address</param>
        public async Task<IResult<bool>> ValidateUrlAsync(string url)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                if (string.IsNullOrEmpty(url))
                    return Result.Fail("Url cannot be null or empty.", false);

                var instaUri = UriCreator.GetValidateReelLinkAddressUri();
                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"url", url},
                };
                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);
                return obj.Status.ToLower() == "ok" ? Result.Success(true) : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        private async Task<IResult<bool>> AppendOrDeleteHighlight(string highlightId, string mediaId, bool delete)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var data = new JObject
                {
                    {"source", "story_viewer"},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()}
                };
                if (delete)
                {
                    data.Add("added_media_ids", "[]");
                    data.Add("removed_media_ids", $"[{ExtensionHelper.EncodeList(new[] { mediaId })}]");
                }
                else
                {
                    data.Add("added_media_ids", $"[{ExtensionHelper.EncodeList(new[] { mediaId })}]");
                    data.Add("removed_media_ids", "[]");
                }
                var instaUri = UriCreator.GetHighlightEditUri(highlightId);
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<bool>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaDefault>(json);
                return obj.Status.ToLower() == "ok" ? Result.Success(true) : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Configure story photo
        /// </summary>
        /// <param name="image">Photo to configure</param>
        /// <param name="uploadId">Upload id</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        private async Task<IResult<InstaStoryMedia>> ConfigureStoryPhotoAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, InstaImage image, string uploadId,
            string caption, Uri uri)
        {
            try
            {
                upProgress.UploadState = InstaUploadState.Configuring;
                progress?.Invoke(upProgress);
                var instaUri = UriCreator.GetStoryConfigureUri();
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"source_type", "1"},
                    {"caption", caption},
                    {"upload_id", uploadId},
                    {"edits", new JObject()},
                    {"disable_comments", false},
                    {"configure_mode", 1},
                    {"camera_position", "unknown"}
                };
                if (uri != null)
                {
                    var webUri = new JArray
                    {
                        new JObject
                        {
                            {"webUri", uri.ToString()}
                        }
                    };
                    var storyCta = new JArray
                    {
                        new JObject
                        {
                            {"links",  webUri}
                        }
                    };
                    data.Add("story_cta", storyCta.ToString(Formatting.None));
                }
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var mediaResponse = JsonConvert.DeserializeObject<InstaStoryMediaResponse>(json);
                    var converter = ConvertersFabric.Instance.GetStoryMediaConverter(mediaResponse);
                    var obj = converter.Convert();
                    upProgress.UploadState = InstaUploadState.Configured;
                    progress?.Invoke(upProgress);

                    upProgress.UploadState = InstaUploadState.Completed;
                    progress?.Invoke(upProgress);
                    return Result.Success(obj);
                }
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaStoryMedia>(exception);
            }
        }
        /// <summary>
        ///     Configure story video
        /// </summary>
        /// <param name="video">Video to configure</param>
        /// <param name="uploadId">Upload id</param>
        /// <param name="caption">Caption</param>
        /// <param name="uri">Uri to add</param>
        private async Task<IResult<InstaStoryMedia>> ConfigureStoryVideoAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, InstaVideoUpload video, string uploadId,
            string caption, Uri uri)
        {
            try
            {
                upProgress.UploadState = InstaUploadState.Configuring;
                progress?.Invoke(upProgress);
                var instaUri = UriCreator.GetVideoStoryConfigureUri(false);
                var rnd = new Random();
                var data = new JObject
                {
                    {"filter_type", "0"},
                    {"timezone_offset", "16200"},
                    {"_csrftoken", _user.CsrfToken},
                    {"client_shared_at", (long.Parse(ApiRequestMessage.GenerateUploadId())- rnd.Next(25,55)).ToString()},
                    {"story_media_creation_date", (long.Parse(ApiRequestMessage.GenerateUploadId())- rnd.Next(50,70)).ToString()},
                    {"media_folder", "Camera"},
                    {"configure_mode", "1"},
                    {"source_type", "4"},
                    {"video_result", ""},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"caption", caption},
                    {"date_time_original", DateTime.Now.ToString("yyyy-dd-MMTh:mm:ss-0fffZ")},
                    {"capture_type", "normal"},
                    {"mas_opt_in", "NOT_PROMPTED"},
                    {"upload_id", uploadId},
                    {"client_timestamp", ApiRequestMessage.GenerateUploadId()},
                    {
                        "device", new JObject{
                            {"manufacturer", _deviceInfo.HardwareManufacturer},
                            {"model", _deviceInfo.DeviceModelIdentifier},
                            {"android_release", _deviceInfo.AndroidVer.VersionNumber},
                            {"android_version", _deviceInfo.AndroidVer.APILevel}
                        }
                    },
                    {"length", 0},
                    {
                        "extra", new JObject
                        {
                            {"source_width", 0},
                            {"source_height", 0}
                        }
                    },
                    {"audio_muted", false},
                    {"poster_frame_index", 0},
                };
                if (uri != null)
                {
                    var webUri = new JArray
                    {
                        new JObject
                        {
                            {"webUri", uri.ToString()}
                        }
                    };
                    var storyCta = new JArray
                    {
                        new JObject
                        {
                            {"links",  webUri}
                        }
                    };
                    data.Add("story_cta", storyCta.ToString(Formatting.None));
                }

                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var uploadParamsObj = new JObject
                {
                    {"num_step_auto_retry", 0},
                    {"num_reupload", 0},
                    {"num_step_manual_retry", 0}
                };
                var uploadParams = JsonConvert.SerializeObject(uploadParamsObj);
                request.Headers.Add("retry_context", uploadParams);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var mediaResponse = JsonConvert.DeserializeObject<InstaStoryMediaResponse>(json);
                    var converter = ConvertersFabric.Instance.GetStoryMediaConverter(mediaResponse);
                    var obj = Result.Success(converter.Convert());
                    upProgress.UploadState = InstaUploadState.Configured;
                    progress?.Invoke(upProgress);
                    upProgress.UploadState = InstaUploadState.Completed;
                    progress?.Invoke(upProgress);
                    return obj;
                }
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.UnExpectedResponse<InstaStoryMedia>(response, json);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaStoryMedia>(exception);
            }
        }
    }
}