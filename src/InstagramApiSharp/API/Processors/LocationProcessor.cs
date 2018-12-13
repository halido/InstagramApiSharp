﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using InstagramApiSharp.API.UriCreators;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Android.DeviceInfo;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.ResponseWrappers;
using InstagramApiSharp.Converters;
using InstagramApiSharp.Helpers;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;

namespace InstagramApiSharp.API.Processors
{
    /// <summary>
    ///     Location api functions.
    /// </summary>
    internal class LocationProcessor : ILocationProcessor
    {
        private readonly AndroidDevice _deviceInfo;
        private readonly IUriCreatorNextId _getFeedUriCreator = new GetLocationFeedUriCreator();
        private readonly HttpHelper _httpHelper;
        private readonly IHttpRequestProcessor _httpRequestProcessor;
        private readonly InstaApi _instaApi;
        private readonly IInstaLogger _logger;
        private readonly IUriCreator _searchLocationUriCreator = new SearchLocationUriCreator();
        private readonly UserSessionData _user;
        private readonly UserAuthValidate _userAuthValidate;
        public LocationProcessor(AndroidDevice deviceInfo, UserSessionData user,
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
        ///     Gets the feed of particular location.
        /// </summary>
        /// <param name="locationId">Location identifier</param>
        /// <param name="paginationParameters">Pagination parameters: next id and max amount of pages to load</param>
        /// <returns>
        ///     Location feed
        /// </returns>
        public async Task<IResult<InstaLocationFeed>> GetLocationFeedAsync(long locationId,
            PaginationParameters paginationParameters)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var uri = _getFeedUriCreator.GetUri(locationId, paginationParameters.NextMaxId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, uri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaLocationFeed>(response, json);

                var feedResponse = JsonConvert.DeserializeObject<InstaLocationFeedResponse>(json);
                var feed = ConvertersFabric.Instance.GetLocationFeedConverter(feedResponse).Convert();
                paginationParameters.PagesLoaded++;
                paginationParameters.NextMaxId = feed.NextMaxId;

                while (feedResponse.MoreAvailable
                       && !string.IsNullOrEmpty(paginationParameters.NextMaxId)
                       && paginationParameters.PagesLoaded < paginationParameters.MaximumPagesToLoad)
                {
                    var nextFeed = await GetLocationFeedAsync(locationId, paginationParameters);
                    if (!nextFeed.Succeeded)
                        return nextFeed;
                    paginationParameters.StartFromMaxId(nextFeed.Value.NextMaxId);
                    paginationParameters.PagesLoaded++;
                    feed.NextMaxId = nextFeed.Value.NextMaxId;
                    feed.Medias.AddRange(nextFeed.Value.Medias);
                    feed.RankedMedias.AddRange(nextFeed.Value.RankedMedias);
                }

                return Result.Success(feed);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaLocationFeed>(exception);
            }
        }

        /// <summary>
        ///     Get location(place) information by external id or facebook places id
        ///     <para>Get external id from this function: <see cref="ILocationProcessor.SearchLocationAsync(double, double, string)"/></para>
        ///     <para>Get facebook places id from this function: <see cref="ILocationProcessor.SearchPlacesAsync(double, double, string)(double, double, string)"/></para>
        /// </summary>
        /// <param name="externalIdOrFacebookPlacesId">
        ///     External id or facebook places id of an location/place
        ///     <para>Get external id from this function: <see cref="ILocationProcessor.SearchLocationAsync(double, double, string)"/></para>
        ///     <para>Get facebook places id from this function: <see cref="ILocationProcessor.SearchPlacesAsync(double, double, string)(double, double, string)"/></para>
        /// </param>
        public async Task<IResult<InstaPlaceShort>> GetLocationInfoAsync(string externalIdOrFacebookPlacesId)
        {
            try
            {
                var instaUri = UriCreator.GetLocationInfoUri(externalIdOrFacebookPlacesId);

                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaPlaceShort>(response, json);

                var obj = JsonConvert.DeserializeObject<InstaPlaceResponse>(json);

                return Result.Success(ConvertersFabric.Instance.GetPlaceShortConverter(obj.Location).Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaPlaceShort>(exception);
            }
        }

        /// <summary>
        ///     Searches for specific location by provided geo-data or search query.
        /// </summary>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="query">Search query</param>
        /// <returns>
        ///     List of locations (short format)
        /// </returns>
        public async Task<IResult<InstaLocationShortList>> SearchLocationAsync(double latitude, double longitude, string query)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var uri = _searchLocationUriCreator.GetUri();

                var fields = new Dictionary<string, string>
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk.ToString()},
                    {"_csrftoken", _user.CsrfToken},
                    {"latitude", latitude.ToString(CultureInfo.InvariantCulture)},
                    {"longitude", longitude.ToString(CultureInfo.InvariantCulture)},
                    {"rank_token", _user.RankToken}
                };

                if (!string.IsNullOrEmpty(query))
                    fields.Add("search_query", query);
                else
                    fields.Add("timestamp", DateTimeHelper.GetUnixTimestampSeconds().ToString());
                if (!Uri.TryCreate(uri, fields.AsQueryString(), out var newuri))
                    return Result.Fail<InstaLocationShortList>("Unable to create uri for location search");

                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, newuri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaLocationShortList>(response, json);
                var locations = JsonConvert.DeserializeObject<InstaLocationSearchResponse>(json);
                var converter = ConvertersFabric.Instance.GetLocationsSearchConverter(locations);
                return Result.Success(converter.Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaLocationShortList>(exception);
            }
        }
        /// <summary>
        ///     Search user by location
        /// </summary>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="desireUsername">Desire username</param>
        /// <param name="count">Maximum user count</param>
        public async Task<IResult<InstaUserSearchLocation>> SearchUserByLocationAsync(double latitude, double longitude, string desireUsername, int count = 50)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var uri = UriCreator.GetUserSearchByLocationUri();
                if (count <= 0)
                    count = 30;
                var fields = new Dictionary<string, string>
                {
                    {"timezone_offset", InstaApiConstants.TIMEZONE_OFFSET.ToString()},
                    {"lat", latitude.ToString(CultureInfo.InvariantCulture)},
                    {"lng", longitude.ToString(CultureInfo.InvariantCulture)},
                    {"count", count.ToString()},
                    {"query", desireUsername},
                    {"context", "blended"},
                    {"rank_token", _user.RankToken}
                };
                if (!Uri.TryCreate(uri, fields.AsQueryString(), out var newuri))
                    return Result.Fail<InstaUserSearchLocation>("Unable to create uri for user search by location");

                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, newuri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaUserSearchLocation>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaUserSearchLocation>(json);
                return obj.Status.ToLower() =="ok"? Result.Success(obj) : Result.UnExpectedResponse<InstaUserSearchLocation>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaUserSearchLocation>(exception);
            }
        }

        /// <summary>
        ///     Search places in facebook
        ///     <para>Note: This works for non-facebook accounts too!</para>
        /// </summary>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <returns>
        ///     <see cref="InstaPlaceList" />
        /// </returns>
        public async Task<IResult<InstaPlaceList>> SearchPlacesAsync(double latitude, double longitude)
        {
            return await SearchPlacesAsync(latitude, longitude, null);
        }

        /// <summary>
        ///     Search places in facebook
        ///     <para>Note: This works for non-facebook accounts too!</para>
        /// </summary>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="query">Query to search (city, country or ...)</param>
        /// <returns>
        ///     <see cref="InstaPlaceList" />
        /// </returns>
        public async Task<IResult<InstaPlaceList>> SearchPlacesAsync(double latitude, double longitude, string query)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                // pagination is not working!
                var paginationParameters = PaginationParameters.MaxPagesToLoad(1);

                InstaPlaceList Convert(InstaPlaceListResponse placelistResponse)
                {
                    return ConvertersFabric.Instance.GetPlaceListConverter(placelistResponse).Convert();
                }
                var places = await SearchPlaces(latitude, longitude, query, paginationParameters);
                if (!places.Succeeded)
                    return Result.Fail<InstaPlaceList>(places.Info.Message);

                var placesResponse = places.Value;
                paginationParameters.NextMaxId = placesResponse.RankToken;
                var pagesLoaded = 1;
                while (placesResponse.HasMore != null 
                      && placesResponse.HasMore.Value
                      && !string.IsNullOrEmpty(placesResponse.RankToken)
                      && pagesLoaded < paginationParameters.MaximumPagesToLoad)
                {
                    var nextPlaces = await SearchPlaces(latitude, longitude, query, paginationParameters);

                    if (!nextPlaces.Succeeded)
                        return Result.Fail(nextPlaces.Info, Convert(nextPlaces.Value));

                    placesResponse.RankToken = paginationParameters.NextMaxId = nextPlaces.Value.RankToken;
                    placesResponse.HasMore = nextPlaces.Value.HasMore;
                    placesResponse.Items.AddRange(nextPlaces.Value.Items);
                    placesResponse.Status = nextPlaces.Value.Status;
                    pagesLoaded++;
                }

                return Result.Success(ConvertersFabric.Instance.GetPlaceListConverter(placesResponse).Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaPlaceList>(exception);
            }
        }

        private async Task<IResult<InstaPlaceListResponse>> SearchPlaces(double latitude, 
            double longitude, 
            string query,
            PaginationParameters paginationParameters)
        {
            try
            {
                if (paginationParameters == null)
                    paginationParameters = PaginationParameters.MaxPagesToLoad(1);

                var instaUri = UriCreator.GetSearchPlacesUri(InstaApiConstants.TIMEZONE_OFFSET,
                    latitude, longitude, query, paginationParameters.NextMaxId);

                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var obj = JsonConvert.DeserializeObject<InstaPlaceListResponse>(json);

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.Fail<InstaPlaceListResponse>(obj.Message);

                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaPlaceListResponse>(exception);
            }
        }
    }
}