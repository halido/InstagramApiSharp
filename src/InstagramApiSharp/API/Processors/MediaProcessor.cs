﻿using System;
using System.Collections.Generic;
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
    ///     Media api functions.
    /// </summary>
    internal class MediaProcessor : IMediaProcessor
    {
        private readonly AndroidDevice _deviceInfo;
        private readonly HttpHelper _httpHelper;
        private readonly IHttpRequestProcessor _httpRequestProcessor;
        private readonly InstaApi _instaApi;
        private readonly IInstaLogger _logger;
        private readonly UserSessionData _user;
        private readonly UserAuthValidate _userAuthValidate;
        public MediaProcessor(AndroidDevice deviceInfo, UserSessionData user,
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
        ///     Delete a media (photo, video or album)
        /// </summary>
        /// <param name="mediaId">The media ID</param>
        /// <param name="mediaType">The type of the media</param>
        /// <returns>Return true if the media is deleted</returns>
        public async Task<IResult<bool>> DeleteMediaAsync(string mediaId, InstaMediaType mediaType)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var deleteMediaUri = UriCreator.GetDeleteMediaUri(mediaId, mediaType);

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"media_id", mediaId}
                };

                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Get, deleteMediaUri, _deviceInfo, data);
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
        ///     Edit the caption/location of the media (photo/video/album)
        /// </summary>
        /// <param name="mediaId">The media ID</param>
        /// <param name="caption">The new caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        /// <param name="userTags">User tags => Optional (ONLY FOR PHOTO POSTS!!!)</param>
        /// <returns>Return true if everything is ok</returns>
        public async Task<IResult<InstaMedia>> EditMediaAsync(string mediaId, string caption, InstaLocationShort location = null, InstaUserTagUpload[] userTags = null)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var editMediaUri = UriCreator.GetEditMediaUri(mediaId);

                var currentMedia = await GetMediaByIdAsync(mediaId);

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"caption_text", caption ?? string.Empty}
                };
                if (location != null)
                {
                    data.Add("location", location.GetJson());
                }

                var removeArr = new JArray();
                if (currentMedia.Succeeded)
                {
                    if (currentMedia.Value?.UserTags != null &&
                        currentMedia.Value.UserTags.Any())
                    {
                        foreach (var user in currentMedia.Value.UserTags)
                            removeArr.Add(user.User.Pk.ToString());
                    }
                }
                if (userTags != null && userTags.Any())
                {
                    var currentDelay = _instaApi.GetRequestDelay();
                    _instaApi.SetRequestDelay(RequestDelay.FromSeconds(1, 2));

                    var tagArr = new JArray();

                    foreach (var tag in userTags)
                    {
                        try
                        {
                            bool tried = false;
                        TryLabel:
                            var u = await _instaApi.UserProcessor.GetUserAsync(tag.Username);
                            if (!u.Succeeded)
                            {
                                if (!tried)
                                {
                                    tried = true;
                                    goto TryLabel;
                                }
                            }
                            else
                            {
                                var position = new JArray(tag.X, tag.Y);
                                var singleTag = new JObject
                                {
                                    {"user_id", u.Value.Pk},
                                    {"position", position}
                                };
                                tagArr.Add(singleTag);
                            }

                        }
                        catch { }
                    }
          
                    _instaApi.SetRequestDelay(currentDelay);
                    var root = new JObject
                    {
                        {"in",  tagArr}
                    };
                    if (removeArr.Any())
                        root.Add("removed", removeArr);

                    data.Add("usertags", root.ToString(Formatting.None));
                }
                else
                {
                    if (removeArr.Any())
                    {
                        var root = new JObject
                        {
                            {"removed", removeArr}
                        };
                        data.Add("usertags", root.ToString(Formatting.None));
                    }
                }
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, editMediaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var mediaResponse = JsonConvert.DeserializeObject<InstaMediaItemResponse>(json,
                        new InstaMediaDataConverter());
                    var converter = ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse);
                    return Result.Success(converter.Convert());
                }
                var error = JsonConvert.DeserializeObject<BadStatusResponse>(json);
                return Result.Fail(error.Message, (InstaMedia)null);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        /// <summary>
        ///     Get blocked medias
        ///     <para>Note: returns media ids!</para>
        /// </summary>
        public async Task<IResult<InstaMediaIdList>> GetBlockedMediasAsync()
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var mediaIds = new InstaMediaIdList();
            try
            {
                var mediaUri = UriCreator.GetBlockedMediaUri();
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, mediaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.Fail($"Error! Status code: {response.StatusCode}", mediaIds);
                var obj = JsonConvert.DeserializeObject<InstaMediaIdsResponse>(json);

                return Result.Success(obj.MediaIds);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail(exception, mediaIds);
            }
        }

        /// <summary>
        ///     Get multiple media by its multiple ids asynchronously
        /// </summary>
        /// <param name="mediaIds">Media ids</param>
        /// <returns>
        ///     <see cref="InstaMediaList" />
        /// </returns>
        public async Task<IResult<InstaMediaList>> GetMediaByIdsAsync(params string[] mediaIds)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var mediaList = new InstaMediaList();
            try
            {
                if (mediaIds?.Length == 0)
                    throw new ArgumentNullException("At least one media id is required");

                var instaUri = UriCreator.GetMediaInfoByMultipleMediaIdsUri(mediaIds,_deviceInfo.DeviceGuid.ToString(), _user.CsrfToken);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaMediaList>(response, json);

                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaListResponse>(json,
                    new InstaMediaListDataConverter());
                mediaList = ConvertersFabric.Instance.GetMediaListConverter(mediaResponse).Convert();    
                
                return Result.Success(mediaList);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail(exception, mediaList);
            }
        }
        /// <summary>
        ///     Get media by its id asynchronously
        /// </summary>
        /// <param name="mediaId">Media id (<see cref="InstaMedia.InstaIdentifier>"/>)</param>
        /// <returns>
        ///     <see cref="InstaMedia" />
        /// </returns>
        public async Task<IResult<InstaMedia>> GetMediaByIdAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var mediaUri = UriCreator.GetMediaUri(mediaId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, mediaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaListResponse>(json,
                    new InstaMediaListDataConverter());
                if (mediaResponse.Medias?.Count > 1)
                {
                    var errorMessage = $"Got wrong media count for request with media id={mediaId}";
                    _logger?.LogInfo(errorMessage);
                    return Result.Fail<InstaMedia>(errorMessage);
                }

                var converter =
                    ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse.Medias.FirstOrDefault());
                return Result.Success(converter.Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        /// <summary>
        ///     Get media ID from an url (got from "share link")
        /// </summary>
        /// <param name="uri">Uri to get media ID</param>
        /// <returns>Media ID</returns>
        public async Task<IResult<string>> GetMediaIdFromUrlAsync(Uri uri)
        {
            try
            {
                var collectionUri = UriCreator.GetMediaIdFromUrlUri(uri);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, collectionUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<string>(response, json);

                var data = JsonConvert.DeserializeObject<InstaOembedUrlResponse>(json);
                return Result.Success(data.MediaId);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<string>(exception);
            }
        }
        /// <summary>
        ///     Get users (short) who liked certain media. Normaly it return around 1000 last users.
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<InstaLikersList>> GetMediaLikersAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var likers = new InstaLikersList();
                var likersUri = UriCreator.GetMediaLikersUri(mediaId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, likersUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaLikersList>(response, json);
                var mediaLikersResponse = JsonConvert.DeserializeObject<InstaMediaLikersResponse>(json);
                likers.UsersCount = mediaLikersResponse.UsersCount;
                if (mediaLikersResponse.UsersCount < 1) return Result.Success(likers);
                likers.AddRange(
                    mediaLikersResponse.Users.Select(ConvertersFabric.Instance.GetUserShortConverter)
                        .Select(converter => converter.Convert()));
                return Result.Success(likers);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaLikersList>(exception);
            }
        }

        /// <summary>
        ///     Get share link from media Id
        /// </summary>
        /// <param name="mediaId">media ID</param>
        /// <returns>Share link as Uri</returns>
        public async Task<IResult<Uri>> GetShareLinkFromMediaIdAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var collectionUri = UriCreator.GetShareLinkFromMediaId(mediaId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, collectionUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<Uri>(response, json);

                var data = JsonConvert.DeserializeObject<InstaPermalinkResponse>(json);
                return Result.Success(new Uri(data.Permalink));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<Uri>(exception);
            }
        }

        /// <summary>
        ///     Like media (photo or video)
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<bool>> LikeMediaAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                return await LikeUnlikeMediaInternal(mediaId, UriCreator.GetLikeMediaUri(mediaId));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Report media
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<bool>> ReportMediaAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetReportMediaUri(mediaId);
                var fields = new Dictionary<string, string>
                {
                    {"media_id", mediaId},
                    {"reason", "1"},
                    {"source_name", "photo_view_profile"},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_csrftoken", _user.CsrfToken}
                };
                var request =
                    _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, fields);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                return response.StatusCode == HttpStatusCode.OK
                    ? Result.Success(true)
                    : Result.UnExpectedResponse<bool>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail(exception, false);
            }
        }

        /// <summary>
        ///     Remove like from media (photo or video)
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<bool>> UnLikeMediaAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                return await LikeUnlikeMediaInternal(mediaId, UriCreator.GetUnLikeMediaUri(mediaId));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        /// <summary>
        ///     Upload album (videos and photos)
        /// </summary>
        /// <param name="images">Array of photos to upload</param>
        /// <param name="videos">Array of videos to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadAlbumAsync(InstaImageUpload[] images, InstaVideoUpload[] videos, string caption, InstaLocationShort location = null)
        {
            return await UploadAlbumAsync(null, images, videos, caption, location);
        }

        /// <summary>
        ///     Upload album (videos and photos)
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="images">Array of photos to upload</param>
        /// <param name="videos">Array of videos to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadAlbumAsync(Action<InstaUploaderProgress> progress, InstaImageUpload[] images, InstaVideoUpload[] videos, string caption, InstaLocationShort location = null)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var upProgress = new InstaUploaderProgress
            {
                Caption = caption ?? string.Empty,
                UploadState = InstaUploadState.Preparing
            };
            try
            {
                upProgress.Name = "Album upload";
                progress?.Invoke(upProgress);
                var imagesUploadIds = new Dictionary<string, InstaImageUpload>();
                var index = 1;
                if (images != null && images.Any())
                {
                    foreach (var image in images)
                    {
                        if (image.UserTags != null && image.UserTags.Any())
                        {
                            var currentDelay = _instaApi.GetRequestDelay();
                            _instaApi.SetRequestDelay(RequestDelay.FromSeconds(1, 2));
                            foreach (var t in image.UserTags)
                            {
                                try
                                {
                                    bool tried = false;
                                TryLabel:
                                    var u = await _instaApi.UserProcessor.GetUserAsync(t.Username);
                                    if (!u.Succeeded)
                                    {
                                        if (!tried)
                                        {
                                            tried = true;
                                            goto TryLabel;
                                        }
                                    }
                                    else
                                        t.Pk = u.Value.Pk;
                                }
                                catch { }
                            }
                            _instaApi.SetRequestDelay(currentDelay);
                        }
                    }
                    foreach (var image in images)
                    {
                        var instaUri = UriCreator.GetUploadPhotoUri();
                        var uploadId = ApiRequestMessage.GenerateUploadId();
                        upProgress.UploadId = uploadId;
                        upProgress.Name = $"[Album] Photo uploading {index}/{images.Length}";
                        upProgress.UploadState = InstaUploadState.Uploading;
                        progress?.Invoke(upProgress);
                        var requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            },
                            {new StringContent("1"), "\"is_sidecar\""}
                        };
                        byte[] fileBytes;
                        if (image.ImageBytes == null)
                            fileBytes = File.ReadAllBytes(image.Uri);
                        else
                            fileBytes = image.ImageBytes;
                        var imageContent = new ByteArrayContent(fileBytes);
                        imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        imageContent.Headers.Add("Content-Type", "application/octet-stream");
                        requestContent.Add(imageContent, "photo",
                            $"pending_media_{ApiRequestMessage.GenerateUploadId()}.jpg");

                        //var progressContent = new ProgressableStreamContent(requestContent, 4096, progress)
                        //{
                        //    UploaderProgress = upProgress
                        //};

                        var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        upProgress.UploadState = InstaUploadState.Uploading;
                        progress?.Invoke(upProgress);
                        var response = await _httpRequestProcessor.SendAsync(request);
                        var json = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            //upProgress = progressContent?.UploaderProgress;
                            upProgress.UploadState = InstaUploadState.Uploaded;
                            progress?.Invoke(upProgress);
                            imagesUploadIds.Add(uploadId, image);
                        }
                        else
                        {
                            upProgress.UploadState = InstaUploadState.Error;
                            progress?.Invoke(upProgress);
                            return Result.UnExpectedResponse<InstaMedia>(response, json);
                        }
                    }
                }

                var videosDic = new Dictionary<string, InstaVideo>();
                var vidIndex = 1;
                if (videos != null && videos.Any())
                {
                    foreach (var video in videos)
                    {
                        var instaUri = UriCreator.GetUploadVideoUri();
                        var uploadId = ApiRequestMessage.GenerateUploadId();
                        upProgress.UploadId = uploadId;
                        upProgress.Name = $"[Album] Video uploading {vidIndex}/{videos.Length}";

                        var requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent("0"), "\"upload_media_height\""},
                            {new StringContent("1"), "\"is_sidecar\""},
                            {new StringContent("0"), "\"upload_media_width\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent("0"), "\"upload_media_duration_ms\""},
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                            {new StringContent("2"), "\"media_type\""},
                        };

                        var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        var response = await _httpRequestProcessor.SendAsync(request);
                        var json = await response.Content.ReadAsStringAsync();
                        var videoResponse = JsonConvert.DeserializeObject<VideoUploadJobResponse>(json);
                        if (videoResponse == null)
                        {
                            upProgress.UploadState = InstaUploadState.Error;
                            progress?.Invoke(upProgress);
                            return Result.Fail<InstaMedia>("Failed to get response from instagram video upload endpoint");
                        }

                        byte[] videoBytes;
                        if (video.Video.VideoBytes == null)
                            videoBytes = File.ReadAllBytes(video.Video.Uri);
                        else
                            videoBytes = video.Video.VideoBytes;
                        var first = videoResponse.VideoUploadUrls[0];
                        instaUri = new Uri(Uri.EscapeUriString(first.Url));


                        requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            }
                        };
                        var videoContent = new ByteArrayContent(videoBytes);
                        videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        videoContent.Headers.Add("Content-Type", "application/octet-stream");
                        videoContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(video.Video.Uri ?? $"C:\\{13.GenerateRandomString()}.mp4")}\"");
                        requestContent.Add(videoContent);
                        //var progressContent = new ProgressableStreamContent(requestContent, 4096, progress)
                        //{
                        //    UploaderProgress = upProgress
                        //};
                        request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        upProgress.UploadState = InstaUploadState.Uploading;
                        progress?.Invoke(upProgress);
                        request.Headers.Host = "upload.instagram.com";
                        request.Headers.Add("Cookie2", "$Version=1");
                        request.Headers.Add("Session-ID", uploadId);
                        request.Headers.Add("job", first.Job);
                        response = await _httpRequestProcessor.SendAsync(request);
                        json = await response.Content.ReadAsStringAsync();
                        upProgress.UploadState = InstaUploadState.Uploaded;
                        progress?.Invoke(upProgress);
                        //upProgress = progressContent?.UploaderProgress;
                        upProgress.UploadState = InstaUploadState.UploadingThumbnail;
                        progress?.Invoke(upProgress);
                        instaUri = UriCreator.GetUploadPhotoUri();
                        requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent("1"), "\"is_sidecar\""},
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            },
                            {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                            {new StringContent("2"), "\"media_type\""},
                        };
                        byte[] imageBytes;
                        if (video.VideoThumbnail.ImageBytes == null)
                            imageBytes = File.ReadAllBytes(video.VideoThumbnail.Uri);
                        else
                            imageBytes = video.VideoThumbnail.ImageBytes;
                        var imageContent = new ByteArrayContent(imageBytes);
                        imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        imageContent.Headers.Add("Content-Type", "application/octet-stream");
                        requestContent.Add(imageContent, "photo", $"cover_photo_{uploadId}.jpg");
                        request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        response = await _httpRequestProcessor.SendAsync(request);
                        json = await response.Content.ReadAsStringAsync();
                        var imgResp = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);
                        videosDic.Add(uploadId, video.Video);

                        upProgress.UploadState = InstaUploadState.Uploaded;
                        progress?.Invoke(upProgress);
                        vidIndex++;
                    }
                }
                var config = await ConfigureAlbumAsync(progress, upProgress, imagesUploadIds, videosDic, caption, location);
                return config;
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        /// <summary>
        ///     Upload album (videos and photos)
        /// </summary>
        /// <param name="album">Array of photos or videos to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadAlbumAsync(InstaAlbumUpload[] album, string caption, InstaLocationShort location = null)
        {
            return await UploadAlbumAsync(null, album, caption, location);
        }

        /// <summary>
        ///     Upload album (videos and photos) with progress
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="album">Array of photos or videos to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadAlbumAsync(Action<InstaUploaderProgress> progress, InstaAlbumUpload[] album, string caption, InstaLocationShort location = null)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var upProgress = new InstaUploaderProgress
            {
                Caption = caption ?? string.Empty,
                UploadState = InstaUploadState.Preparing
            };
            try
            {
                upProgress.Name = "Album upload";
                progress?.Invoke(upProgress);
                var uploadIds = new Dictionary<string, InstaAlbumUpload>();
                var index = 1;

                foreach (var al in album)
                {
                    if (al.IsImage)
                    {
                        var image = al.ImageToUpload;
                        if (image.UserTags != null && image.UserTags.Any())
                        {
                            var currentDelay = _instaApi.GetRequestDelay();
                            _instaApi.SetRequestDelay(RequestDelay.FromSeconds(1, 2));
                            foreach (var t in image.UserTags)
                            {
                                try
                                {
                                    bool tried = false;
                                TryLabel:
                                    var u = await _instaApi.UserProcessor.GetUserAsync(t.Username);
                                    if (!u.Succeeded)
                                    {
                                        if (!tried)
                                        {
                                            tried = true;
                                            goto TryLabel;
                                        }
                                    }
                                    else
                                        t.Pk = u.Value.Pk;
                                }
                                catch { }
                            }
                            _instaApi.SetRequestDelay(currentDelay);
                        }
                    }
                }
                foreach (var al in album)
                {
                    if (al.IsImage)
                    {
                        upProgress.Name = $"[Album] uploading {index}/{album.Length}";
                        upProgress.UploadState = InstaUploadState.Uploading;
                        progress?.Invoke(upProgress);
                        var image = await UploadSinglePhoto(progress, al.ImageToUpload, upProgress);
                        if (image.Succeeded)
                            uploadIds.Add(image.Value, al);
                    }
                    else if (al.IsVideo)
                    {
                        upProgress.Name = $"[Album] uploading {index}/{album.Length}";
                        upProgress.UploadState = InstaUploadState.Uploading;
                        progress?.Invoke(upProgress);
                        var video = await UploadSingleVideo(progress, al.VideoToUpload, upProgress);
                        if (video.Succeeded)
                            uploadIds.Add(video.Value, al);
                    }
                    index++;
                }
                var config = await ConfigureAlbumAsync(progress, upProgress, uploadIds, caption, location);
                return config;
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        
        private async Task<IResult<string>> UploadSinglePhoto(Action<InstaUploaderProgress> progress, InstaImageUpload image, InstaUploaderProgress upProgress)
        {
            var instaUri = UriCreator.GetUploadPhotoUri();
            var uploadId = ApiRequestMessage.GenerateUploadId();
            //upProgress.UploadId = uploadId;
            //upProgress.Name = $"[Album] Photo uploading {index}/{images.Length}";
            //upProgress.UploadState = InstaUploadState.Uploading;
            //progress?.Invoke(upProgress);
            var requestContent = new MultipartFormDataContent(uploadId)
            {
                {new StringContent(uploadId), "\"upload_id\""},
                {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                {
                    new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                    "\"image_compression\""
                },
                {new StringContent("1"), "\"is_sidecar\""}
            };
            byte[] fileBytes;
            if (image.ImageBytes == null)
                fileBytes = File.ReadAllBytes(image.Uri);
            else
                fileBytes = image.ImageBytes;
            var imageContent = new ByteArrayContent(fileBytes);
            imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
            imageContent.Headers.Add("Content-Type", "application/octet-stream");
            requestContent.Add(imageContent, "photo",
                $"pending_media_{ApiRequestMessage.GenerateUploadId()}.jpg");

            //var progressContent = new ProgressableStreamContent(requestContent, 4096, progress)
            //{
            //    UploaderProgress = upProgress
            //};

            var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
            request.Content = requestContent;
            upProgress.UploadState = InstaUploadState.Uploading;
            progress?.Invoke(upProgress);
            var response = await _httpRequestProcessor.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                //upProgress = progressContent?.UploaderProgress;
                upProgress.UploadState = InstaUploadState.Uploaded;
                progress?.Invoke(upProgress);
                return Result.Success(uploadId);
            }
            else
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.Fail<string>("NO UPLOAD ID");
            }
        }

        private async Task<IResult<string>> UploadSingleVideo(Action<InstaUploaderProgress> progress, InstaVideoUpload video, InstaUploaderProgress upProgress)
        {
            var instaUri = UriCreator.GetUploadVideoUri();
            var uploadId = ApiRequestMessage.GenerateUploadId();
            //upProgress.UploadId = uploadId;
            //upProgress.Name = $"[Album] Video uploading {vidIndex}/{videos.Length}";

            var requestContent = new MultipartFormDataContent(uploadId)
            {
                {new StringContent("0"), "\"upload_media_height\""},
                {new StringContent("1"), "\"is_sidecar\""},
                {new StringContent("0"), "\"upload_media_width\""},
                {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                {new StringContent("0"), "\"upload_media_duration_ms\""},
                {new StringContent(uploadId), "\"upload_id\""},
                {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                {new StringContent("2"), "\"media_type\""},
            };

            var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
            request.Content = requestContent;
            var response = await _httpRequestProcessor.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var videoResponse = JsonConvert.DeserializeObject<VideoUploadJobResponse>(json);
            if (videoResponse == null)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.Fail<string>("Failed to get response from instagram video upload endpoint");
            }

            byte[] videoBytes;
            if (video.Video.VideoBytes == null)
                videoBytes = File.ReadAllBytes(video.Video.Uri);
            else
                videoBytes = video.Video.VideoBytes;
            var first = videoResponse.VideoUploadUrls[0];
            instaUri = new Uri(Uri.EscapeUriString(first.Url));


            requestContent = new MultipartFormDataContent(uploadId)
            {
                {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                {
                    new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                    "\"image_compression\""
                }
            };
            var videoContent = new ByteArrayContent(videoBytes);
            videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
            videoContent.Headers.Add("Content-Type", "application/octet-stream");
            videoContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(video.Video.Uri ?? $"C:\\{13.GenerateRandomString()}.mp4")}\"");
            requestContent.Add(videoContent);
            //var progressContent = new ProgressableStreamContent(requestContent, 4096, progress)
            //{
            //    UploaderProgress = upProgress
            //};
            request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
            request.Content = requestContent;
            upProgress.UploadState = InstaUploadState.Uploading;
            progress?.Invoke(upProgress);
            request.Headers.Host = "upload.instagram.com";
            request.Headers.Add("Cookie2", "$Version=1");
            request.Headers.Add("Session-ID", uploadId);
            request.Headers.Add("job", first.Job);
            response = await _httpRequestProcessor.SendAsync(request);
            json = await response.Content.ReadAsStringAsync();
            upProgress.UploadState = InstaUploadState.Uploaded;
            progress?.Invoke(upProgress);
            //upProgress = progressContent?.UploaderProgress;
            upProgress.UploadState = InstaUploadState.UploadingThumbnail;
            progress?.Invoke(upProgress);
            instaUri = UriCreator.GetUploadPhotoUri();
            requestContent = new MultipartFormDataContent(uploadId)
            {
                {new StringContent("1"), "\"is_sidecar\""},
                {new StringContent(uploadId), "\"upload_id\""},
                {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                {
                    new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                    "\"image_compression\""
                },
                {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                {new StringContent("2"), "\"media_type\""},
            };
            byte[] imageBytes;
            if (video.VideoThumbnail.ImageBytes == null)
                imageBytes = File.ReadAllBytes(video.VideoThumbnail.Uri);
            else
                imageBytes = video.VideoThumbnail.ImageBytes;
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
            imageContent.Headers.Add("Content-Type", "application/octet-stream");
            requestContent.Add(imageContent, "photo", $"cover_photo_{uploadId}.jpg");
            request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
            request.Content = requestContent;
            response = await _httpRequestProcessor.SendAsync(request);
            json = await response.Content.ReadAsStringAsync();
            var imgResp = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);
            upProgress.UploadState = InstaUploadState.Uploaded;
            progress?.Invoke(upProgress);

            return Result.Success(uploadId);
        }


        private async Task<IResult<InstaMedia>> ConfigureAlbumAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, Dictionary<string, InstaAlbumUpload> album, string caption, InstaLocationShort location)
        {
            try
            {
                upProgress.Name = "Album upload";
                upProgress.UploadState = InstaUploadState.Configuring;
                progress?.Invoke(upProgress);
                var instaUri = UriCreator.GetMediaAlbumConfigureUri();
                var clientSidecarId = ApiRequestMessage.GenerateUploadId();
                var childrenArray = new JArray();
                
                foreach(var al in album)
                {
                    if (al.Value.IsImage)
                        childrenArray.Add(GetImageConfigure(al.Key, al.Value.ImageToUpload));
                    else if (al.Value.IsVideo)
                        childrenArray.Add(GetVideoConfigure(al.Key, al.Value.VideoToUpload.Video));
                }

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"caption", caption},
                    {"client_sidecar_id", clientSidecarId},
                    {"upload_id", clientSidecarId},
                    {
                        "device", new JObject
                        {
                            {"manufacturer", _deviceInfo.HardwareManufacturer},
                            {"model", _deviceInfo.DeviceModelIdentifier},
                            {"android_release", _deviceInfo.AndroidVer.VersionNumber},
                            {"android_version", _deviceInfo.AndroidVer.APILevel}
                        }
                    },
                    {"children_metadata", childrenArray},
                };
                if (location != null)
                {
                    data.Add("location", location.GetJson());
                    data.Add("date_time_digitalized", DateTime.Now.ToString("yyyy:dd:MM+h:mm:ss"));
                }
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                }
                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaAlbumResponse>(json);
                var converter = ConvertersFabric.Instance.GetSingleMediaFromAlbumConverter(mediaResponse);
                var obj = converter.Convert();
                if (obj.Caption == null && !string.IsNullOrEmpty(caption))
                {
                    var editedMedia = await _instaApi.MediaProcessor.EditMediaAsync(obj.InstaIdentifier, caption, location);
                    if (editedMedia.Succeeded)
                    {
                        upProgress.UploadState = InstaUploadState.Configured;
                        progress?.Invoke(upProgress);
                        upProgress.UploadState = InstaUploadState.Completed;
                        progress?.Invoke(upProgress);
                        return Result.Success(editedMedia.Value);
                    }
                }
                upProgress.UploadState = InstaUploadState.Configured;
                progress?.Invoke(upProgress);
                upProgress.UploadState = InstaUploadState.Completed;
                progress?.Invoke(upProgress);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }




        /// <summary>
        ///     Upload photo [Supports user tags]
        /// </summary>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadPhotoAsync(InstaImageUpload image, string caption, InstaLocationShort location = null)
        {
            return await UploadPhotoAsync(null, image, caption, location);
        }

        /// <summary>
        ///     Upload photo with progress [Supports user tags]
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadPhotoAsync(Action<InstaUploaderProgress> progress, InstaImageUpload image, string caption,
            InstaLocationShort location = null)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            return await _instaApi.HelperProcessor.SendMediaPhotoAsync(progress, image, caption, location);
        }

        /// <summary>
        ///     Upload video
        /// </summary>
        /// <param name="video">Video and thumbnail to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadVideoAsync(InstaVideoUpload video, string caption, InstaLocationShort location = null)
        {
            return await UploadVideoAsync(null, video, caption, location);
        }
        /// <summary>
        ///     Upload video with progress
        /// </summary>
        /// <param name="progress">Progress action</param>
        /// <param name="video">Video and thumbnail to upload</param>
        /// <param name="caption">Caption</param>
        /// <param name="location">Location => Optional (get it from <seealso cref="LocationProcessor.SearchLocationAsync"/></param>
        public async Task<IResult<InstaMedia>> UploadVideoAsync(Action<InstaUploaderProgress> progress, InstaVideoUpload video, string caption, InstaLocationShort location = null)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            var upProgress = new InstaUploaderProgress
            {
                Caption = caption ?? string.Empty,
                UploadState = InstaUploadState.Preparing
            };
            try
            {
                var instaUri = UriCreator.GetUploadVideoUri();
                var uploadId = ApiRequestMessage.GenerateUploadId();
                upProgress.UploadId = uploadId;
                progress?.Invoke(upProgress);
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent("2"), "\"media_type\""},
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };

                var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                var videoResponse = JsonConvert.DeserializeObject<VideoUploadJobResponse>(json);
                if (videoResponse == null)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.Fail<InstaMedia>("Failed to get response from instagram video upload endpoint");
                }

                byte[] fileBytes;
                if (video.Video.VideoBytes == null)
                    fileBytes = File.ReadAllBytes(video.Video.Uri);
                else
                    fileBytes = video.Video.VideoBytes;
                var first = videoResponse.VideoUploadUrls[0];
                instaUri = new Uri(Uri.EscapeUriString(first.Url));


                requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };


                var videoContent = new ByteArrayContent(fileBytes);
                videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
                videoContent.Headers.Add("Content-Type", "application/octet-stream");
                videoContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(video.Video.Uri ?? $"C:\\{13.GenerateRandomString()}.mp4")}\"");
                requestContent.Add(videoContent);

                //var progressContent = new ProgressableStreamContent(requestContent, 4096, progress)
                //{
                //    UploaderProgress = upProgress
                //};
                
                request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                upProgress.UploadState = InstaUploadState.Uploading;
                progress?.Invoke(upProgress);
                request.Headers.Host = "upload.instagram.com";
                request.Headers.Add("Cookie2", "$Version=1");
                request.Headers.Add("Session-ID", uploadId);
                request.Headers.Add("job", first.Job);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();
                //upProgress = progressContent.UploaderProgress;
                upProgress.UploadState = InstaUploadState.Uploaded;
                progress?.Invoke(upProgress);
                await UploadVideoThumbnailAsync(progress, upProgress, video.VideoThumbnail, uploadId);

                return await ConfigureVideoAsync(progress, upProgress, video.Video, uploadId, caption, location);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<InstaMedia>> ConfigureAlbumAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, Dictionary<string, InstaImageUpload> imagesUploadIds, Dictionary<string, InstaVideo> videos, string caption, InstaLocationShort location)
        {
            try
            {
                upProgress.Name = "Album upload";
                upProgress.UploadState = InstaUploadState.Configuring;
                progress?.Invoke(upProgress);
                var instaUri = UriCreator.GetMediaAlbumConfigureUri();
                var clientSidecarId = ApiRequestMessage.GenerateUploadId();
                var childrenArray = new JArray();
                if (imagesUploadIds != null && imagesUploadIds.Any())
                {
                    foreach (var img in imagesUploadIds)
                    {
                        childrenArray.Add(GetImageConfigure(img.Key, img.Value));
                    }
                }
                if (videos != null && videos.Any())
                {
                    foreach (var id in videos)
                    {
                        childrenArray.Add(GetVideoConfigure(id.Key, id.Value));
                    }
                }
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"caption", caption},
                    {"client_sidecar_id", clientSidecarId},
                    {"upload_id", clientSidecarId},
                    {
                        "device", new JObject
                        {
                            {"manufacturer", _deviceInfo.HardwareManufacturer},
                            {"model", _deviceInfo.DeviceModelIdentifier},
                            {"android_release", _deviceInfo.AndroidVer.VersionNumber},
                            {"android_version", _deviceInfo.AndroidVer.APILevel}
                        }
                    },
                    {"children_metadata", childrenArray},
                };
                if (location != null)
                {
                    data.Add("location", location.GetJson());
                    data.Add("date_time_digitalized", DateTime.Now.ToString("yyyy:dd:MM+h:mm:ss"));
                }
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                }
                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaAlbumResponse>(json);
                var converter = ConvertersFabric.Instance.GetSingleMediaFromAlbumConverter(mediaResponse);
                var obj = converter.Convert();
                if (obj.Caption == null && !string.IsNullOrEmpty(caption))
                {
                    var editedMedia = await _instaApi.MediaProcessor.EditMediaAsync(obj.InstaIdentifier, caption, location);
                    if (editedMedia.Succeeded)
                    {
                        upProgress.UploadState = InstaUploadState.Configured;
                        progress?.Invoke(upProgress);
                        upProgress.UploadState = InstaUploadState.Completed;
                        progress?.Invoke(upProgress);
                        return Result.Success(editedMedia.Value);
                    }
                }
                upProgress.UploadState = InstaUploadState.Configured;
                progress?.Invoke(upProgress);
                upProgress.UploadState = InstaUploadState.Completed;
                progress?.Invoke(upProgress);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<InstaMedia>> ConfigureVideoAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, InstaVideo video, string uploadId, string caption, InstaLocationShort location)
        {
            try
            {
                upProgress.UploadState = InstaUploadState.Configuring;
                progress?.Invoke(upProgress);
                var instaUri = UriCreator.GetMediaConfigureUri();
                var data = new JObject
                {
                    {"caption", caption ?? string.Empty},
                    {"upload_id", uploadId},
                    {"source_type", "3"},
                    {"camera_position", "unknown"},
                    {
                        "extra", new JObject
                        {
                            {"source_width", 0},
                            {"source_height", 0}
                        }
                    },
                    {
                        "clips", new JArray{
                            new JObject
                            {
                                {"length", 0},
                                {"creation_date", DateTime.Now.ToString("yyyy-dd-MMTh:mm:ss-0fff")},
                                {"source_type", "3"},
                                {"camera_position", "back"}
                            }
                        }
                    },
                    {"poster_frame_index", 0},
                    {"audio_muted", false},
                    {"filter_type", "0"},
                    {"video_result", ""},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.UserName}
                };
                if (location != null)
                {
                    data.Add("location", location.GetJson());
                    data.Add("date_time_digitalized", DateTime.Now.ToString("yyyy:dd:MM+h:mm:ss"));
                }
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Host = "i.instagram.com";
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    upProgress.UploadState = InstaUploadState.Error;
                    progress?.Invoke(upProgress);
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                }
                upProgress.UploadState = InstaUploadState.Configured;
                progress?.Invoke(upProgress);

                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaItemResponse>(json,
                                    new InstaMediaDataConverter());
                var converter = ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse);
                var obj = converter.Convert();
                if (obj.Caption == null && !string.IsNullOrEmpty(caption))
                {
                    var editedMedia = await _instaApi.MediaProcessor.EditMediaAsync(obj.InstaIdentifier, caption, location);
                    if (editedMedia.Succeeded)
                        return Result.Success(editedMedia.Value);
                }
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Deprecated
        /// </summary>
        /// <param name="uploadId"></param>
        /// <param name="caption"></param>
        /// <param name="location"></param>
        /// <returns></returns>
        private async Task<IResult<InstaMedia>> ExposeVideoAsync(string uploadId, string caption, InstaLocationShort location)
        {
            try
            {
                var instaUri = UriCreator.GetMediaConfigureUri();
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"experiment", "ig_android_profile_contextual_feed"},
                    {"id", _user.LoggedInUser.Pk},
                    {"upload_id", uploadId},

                };

                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Host = "i.instagram.com";
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var jObject = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);

                if (response.IsSuccessStatusCode)
                {
                    var mediaResponse = JsonConvert.DeserializeObject<InstaMediaItemResponse>(json,
                        new InstaMediaDataConverter());
                    var converter = ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse);
                    var obj = converter.Convert();
                    if (obj.Caption == null && !string.IsNullOrEmpty(caption))
                    {
                        var editedMedia = await _instaApi.MediaProcessor.EditMediaAsync(obj.InstaIdentifier, caption, location);
                        if (editedMedia.Succeeded)
                            return Result.Success(editedMedia.Value);
                    }
                    return Result.Success(obj);
                }

                return Result.Fail<InstaMedia>(jObject.Status);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<bool>> LikeUnlikeMediaInternal(string mediaId, Uri instaUri)
        {
            var fields = new Dictionary<string, string>
            {
                {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                {"_uid", _user.LoggedInUser.Pk.ToString()},
                {"_csrftoken", _user.CsrfToken},
                {"media_id", mediaId}
            };
            var request =
                _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, fields);
            var response = await _httpRequestProcessor.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            return response.StatusCode == HttpStatusCode.OK
                ? Result.Success(true)
                : Result.UnExpectedResponse<bool>(response, json);
        }

        private async Task<IResult<bool>> UploadVideoThumbnailAsync(Action<InstaUploaderProgress> progress, InstaUploaderProgress upProgress, InstaImage image, string uploadId)
        {
            try
            {
                var instaUri = UriCreator.GetUploadPhotoUri();
                upProgress.UploadState = InstaUploadState.UploadingThumbnail;
                progress?.Invoke(upProgress);
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };
                byte[] fileBytes;
                if (image.ImageBytes == null)
                    fileBytes = File.ReadAllBytes(image.Uri);
                else
                    fileBytes = image.ImageBytes;

                var imageContent = new ByteArrayContent(fileBytes);
                imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                imageContent.Headers.Add("Content-Type", "application/octet-stream");
                requestContent.Add(imageContent, "photo", $"pending_media_{uploadId}.jpg");
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var imgResp = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);
                if (imgResp.Status.ToLower() == "ok")
                {
                    upProgress.UploadState = InstaUploadState.ThumbnailUploaded;
                    progress?.Invoke(upProgress);
                    return Result.Success(true);
                }

                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                return Result.Fail<bool>("Could not upload thumbnail");
            }
            catch (Exception exception)
            {
                upProgress.UploadState = InstaUploadState.Error;
                progress?.Invoke(upProgress);
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }


        JObject GetImageConfigure(string uploadId, InstaImageUpload image)
        {
            var imgData = new JObject
            {
                {"timezone_offset", InstaApiConstants.TIMEZONE_OFFSET.ToString()},
                {"source_type", 4},
                {"upload_id", uploadId},
                {"caption", ""},
            };
            if (image.UserTags != null && image.UserTags.Any())
            {
                var tagArr = new JArray();
                foreach (var tag in image.UserTags)
                {
                    if (tag.Pk != -1)
                    {
                        var position = new JArray(tag.X, tag.Y);
                        var singleTag = new JObject
                                    {
                                        {"user_id", tag.Pk},
                                        {"position", position}
                                    };
                        tagArr.Add(singleTag);
                    }
                }

                var root = new JObject
                {
                    {"in",  tagArr}
                };
                imgData.Add("usertags", root.ToString(Formatting.None));
            }
            return imgData;
        }

        JObject GetVideoConfigure(string uploadId, InstaVideo video)
        {
            var vidData = new JObject
            {
                {"timezone_offset", InstaApiConstants.TIMEZONE_OFFSET.ToString()},
                {"caption", ""},
                {"upload_id", uploadId},
                {"date_time_original", DateTime.Now.ToString("yyyy-dd-MMTh:mm:ss-0fffZ")},
                {"source_type", "4"},
                {
                    "extra", JsonConvert.SerializeObject(new JObject
                    {
                        {"source_width", 0},
                        {"source_height", 0}
                    })
                },
                {
                    "clips", JsonConvert.SerializeObject(new JArray{
                        new JObject
                        {
                            {"length", video.Length},
                            {"source_type", "4"},
                        }
                    })
                },
                {
                    "device", JsonConvert.SerializeObject(new JObject{
                        {"manufacturer", _deviceInfo.HardwareManufacturer},
                        {"model", _deviceInfo.DeviceModelIdentifier},
                        {"android_release", _deviceInfo.AndroidVer.VersionNumber},
                        {"android_version", _deviceInfo.AndroidVer.APILevel}
                    })
                },
                {"length", video.Length},
                {"poster_frame_index", 0},
                {"audio_muted", false},
                {"filter_type", "0"},
                {"video_result", "deprecated"},
            };
            return vidData;
        }
    }
}