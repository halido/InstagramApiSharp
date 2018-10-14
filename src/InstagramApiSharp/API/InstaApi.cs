﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using InstagramApiSharp.API.Processors;
using InstagramApiSharp.API.Versions;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Android.DeviceInfo;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.ResponseWrappers;
using InstagramApiSharp.Classes.ResponseWrappers.BaseResponse;
using InstagramApiSharp.Converters;
using InstagramApiSharp.Enums;
using InstagramApiSharp.Helpers;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InstagramApiSharp.API
{
    /// <summary>
    ///     Base of everything that you want.
    /// </summary>
    internal class InstaApi : IInstaApi
    {
        #region Variables and properties

        private IRequestDelay _delay = RequestDelay.Empty();
        private readonly IHttpRequestProcessor _httpRequestProcessor;
        private readonly IInstaLogger _logger;
        private InstaApiVersionType _apiVersionType;
        private InstaApiVersion _apiVersion;
        private HttpHelper _httpHelper;
        private AndroidDevice _deviceInfo;
        private InstaTwoFactorLoginInfo _twoFactorInfo;
        private InstaChallengeLoginInfo _challengeinfo;
        private UserSessionData _userSession;
        private UserSessionData _user
        {
            get { return _userSession; }
            set { _userSession = value; _userAuthValidate.User = value; }
        }
        private UserAuthValidate _userAuthValidate;
        bool IsCustomDeviceSet = false;

        string _waterfallIdReg = "", _deviceIdReg = "", _phoneIdReg = "", _guidReg = "";
        InstaAccountRegistrationPhoneNumber _signUpPhoneNumberInfo;

        private bool _isUserAuthenticated;
        /// <summary>
        ///     Indicates whether user authenticated or not
        /// </summary>
        public bool IsUserAuthenticated
        {
            get { return _isUserAuthenticated; }
            internal set { _isUserAuthenticated = value; _userAuthValidate.IsUserAuthenticated = value; }
        }

        #endregion Variables and properties

        #region Processors

        private ICollectionProcessor _collectionProcessor;
        private ICommentProcessor _commentProcessor;
        private IFeedProcessor _feedProcessor;
        private IHashtagProcessor _hashtagProcessor;
        private ILocationProcessor _locationProcessor;
        private IMediaProcessor _mediaProcessor;
        private IMessagingProcessor _messagingProcessor;
        private IStoryProcessor _storyProcessor;
        private IUserProcessor _userProcessor;
        private ILiveProcessor _liveProcessor;
        private IDiscoverProcessor _discoverProcessor;
        private IAccountProcessor _accountProcessor;
        ITVProcessor _tvProcessor;
        HelperProcessor _helperProcessor;
        IBusinessProcessor _businessProcessor;
        /// <summary>
        ///     Live api functions.
        /// </summary>
        public ILiveProcessor LiveProcessor => _liveProcessor;
        /// <summary>
        ///     Discover api functions.
        /// </summary>
        public IDiscoverProcessor DiscoverProcessor => _discoverProcessor;
        /// <summary>
        ///     Account api functions.
        /// </summary>
        public IAccountProcessor AccountProcessor => _accountProcessor;
        /// <summary>
        ///     Comments api functions.
        /// </summary>
        public ICommentProcessor CommentProcessor => _commentProcessor;
        /// <summary>
        ///     Story api functions.
        /// </summary>
        public IStoryProcessor StoryProcessor => _storyProcessor;
        /// <summary>
        ///     Media api functions.
        /// </summary>
        public IMediaProcessor MediaProcessor => _mediaProcessor;
        /// <summary>
        ///     Messaging (direct) api functions.
        /// </summary>
        public IMessagingProcessor MessagingProcessor => _messagingProcessor;
        /// <summary>
        ///     Feed api functions.
        /// </summary>
        public IFeedProcessor FeedProcessor => _feedProcessor;
        /// <summary>
        ///     Collection api functions.
        /// </summary>
        public ICollectionProcessor CollectionProcessor => _collectionProcessor;
        /// <summary>
        /// Location api functions.
        /// </summary>
        public ILocationProcessor LocationProcessor => _locationProcessor;
        /// <summary>
        ///     Hashtag api functions.
        /// </summary>
        public IHashtagProcessor HashtagProcessor => _hashtagProcessor;
        /// <summary>
        ///     User api functions.
        /// </summary>
        public IUserProcessor UserProcessor => _userProcessor;
        /// <summary>
        ///     Helper processor for other processors
        /// </summary>
        internal HelperProcessor HelperProcessor => _helperProcessor;
        /// <summary>
        ///     Instagram TV api functions
        /// </summary>
        public ITVProcessor TVProcessor => _tvProcessor;
        /// <summary>
        ///     Business api functions
        ///     <para>Note: All functions of this interface only works with business accounts!</para>
        /// </summary>
        public IBusinessProcessor BusinessProcessor => _businessProcessor;

        #endregion Processors

        #region Constructor

        public InstaApi(UserSessionData user, IInstaLogger logger, AndroidDevice deviceInfo,
            IHttpRequestProcessor httpRequestProcessor, InstaApiVersionType apiVersionType)
        {
            _userAuthValidate = new UserAuthValidate();
            _user = user;
            _logger = logger;
            _deviceInfo = deviceInfo;
            _httpRequestProcessor = httpRequestProcessor;
            _apiVersionType = apiVersionType;
            _apiVersion = InstaApiVersionList.GetApiVersionList().GetApiVersion(apiVersionType);
            _httpHelper = new HttpHelper(_apiVersion);
        }
        
        #endregion Constructor

        #region Register new account with Phone number and email

        /// <summary>
        ///     Check email availability
        /// </summary>
        /// <param name="email">Email to check</param>
        public async Task<IResult<InstaCheckEmailRegistration>> CheckEmailAsync(string email)
        {
            return await CheckEmail(email);
        }
        private async Task<IResult<InstaCheckEmailRegistration>> CheckEmail(string email, bool useNewWaterfall = true)
        {
            try
            {
                if (_waterfallIdReg == null || useNewWaterfall)
                    _waterfallIdReg = Guid.NewGuid().ToString();

                var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                var cookies = 
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                
                var postData = new Dictionary<string, string>
                {
                    {"_csrftoken",      csrftoken},
                    {"login_nonces",    "[]"},
                    {"email",           email},
                    {"qe_id",           Guid.NewGuid().ToString()},
                    {"waterfall_id",    _waterfallIdReg},
                };
                var instaUri = UriCreator.GetCheckEmailUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var obj = JsonConvert.DeserializeObject<InstaCheckEmailRegistration>(json);
                    if (obj.ErrorType == "fail")
                        return Result.UnExpectedResponse<InstaCheckEmailRegistration>(response, json);
                    if (obj.ErrorType == "email_is_taken")
                        return Result.Fail("Email is taken.", (InstaCheckEmailRegistration)null);
                    if (obj.ErrorType == "invalid_email")
                        return Result.Fail("Please enter a valid email address.", (InstaCheckEmailRegistration)null);

                    return Result.UnExpectedResponse<InstaCheckEmailRegistration>(response, json);
                }
                else
                {
                    var obj = JsonConvert.DeserializeObject<InstaCheckEmailRegistration>(json);
                    if(obj.ErrorType == "fail")
                        return Result.UnExpectedResponse<InstaCheckEmailRegistration>(response, json);
                    if (obj.ErrorType == "email_is_taken")
                        return Result.Fail("Email is taken.", (InstaCheckEmailRegistration)null);
                    if (obj.ErrorType == "invalid_email")
                        return Result.Fail("Please enter a valid email address.", (InstaCheckEmailRegistration)null);

                    return Result.Success(obj);
                }
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaCheckEmailRegistration>(exception);
            }
        }
        /// <summary>
        ///     Check phone number availability
        /// </summary>
        /// <param name="phoneNumber">Phone number to check</param>
        public async Task<IResult<bool>> CheckPhoneNumberAsync(string phoneNumber)
        {
            try
            {
                _deviceIdReg = ApiRequestMessage.GenerateDeviceId();
       
                var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                
                var postData = new Dictionary<string, string>
                {
                    {"_csrftoken",      csrftoken},
                    {"login_nonces",    "[]"},
                    {"phone_number",    phoneNumber},
                    {"device_id",    _deviceInfo.DeviceId},
                };
                var instaUri = UriCreator.GetCheckPhoneNumberUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return Result.UnExpectedResponse<bool>(response, json);
                }

                return Result.Success(true);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Check username availablity. 
        /// </summary>
        /// <param name="username">Username</param>
        public async Task<IResult<InstaAccountCheck>> CheckUsernameAsync(string username)
        {
            try
            {
                var instaUri = UriCreator.GetCheckUsernameUri();
                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"username", username}
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var obj = JsonConvert.DeserializeObject<InstaAccountCheck>(json);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaAccountCheck>(exception);
            }
        }
        /// <summary>
        ///     Send sign up sms code
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        public async Task<IResult<bool>> SendSignUpSmsCodeAsync(string phoneNumber)
        {
            try
            {
                if (string.IsNullOrEmpty(_waterfallIdReg))
                    _waterfallIdReg = Guid.NewGuid().ToString();

                await CheckPhoneNumberAsync(phoneNumber);

                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var postData = new Dictionary<string, string>
                {
                    {"phone_id",        _deviceInfo.PhoneGuid.ToString()},
                    {"phone_number",    phoneNumber},
                    {"_csrftoken",      csrftoken},
                    {"guid",            _deviceInfo.DeviceGuid.ToString()},
                    {"device_id",       _deviceInfo.DeviceId},
                    {"waterfall_id",    _waterfallIdReg},
                };
                var instaUri = UriCreator.GetSignUpSMSCodeUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var o = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumber>(json);

                    return Result.UnExpectedResponse<bool>(response, o.Message?.Errors?[0], json);
                }
                _signUpPhoneNumberInfo = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumber>(json);
                return Result.Success(true);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Verify sign up sms code
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        /// <param name="verificationCode">Verification code</param>
        public async Task<IResult<InstaPhoneNumberRegistration>> VerifySignUpSmsCodeAsync(string phoneNumber, string verificationCode)
        {
            try
            {
                if (string.IsNullOrEmpty(_waterfallIdReg))
                    throw new ArgumentException("You should call SendSignUpSmsCodeAsync function first.");

                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var postData = new Dictionary<string, string>
                {
                    {"verification_code",         verificationCode},
                    {"phone_number",              phoneNumber},
                    {"_csrftoken",                csrftoken},
                    {"guid",                      _deviceInfo.DeviceGuid.ToString()},
                    {"device_id",                 _deviceInfo.DeviceId},
                    {"waterfall_id",              _waterfallIdReg},
                };
                var instaUri = UriCreator.GetValidateSignUpSMSCodeUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var o = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumberVerifySms>(json);

                    return Result.Fail(o.Errors?.Nonce?[0], (InstaPhoneNumberRegistration)null);
                }

                var r = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumberVerifySms>(json);
                if(r.ErrorType == "invalid_nonce")
                    return Result.Fail(r.Errors?.Nonce?[0], (InstaPhoneNumberRegistration)null);

                await GetRegistrationStepsAsync();
                var obj = JsonConvert.DeserializeObject<InstaPhoneNumberRegistration>(json);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaPhoneNumberRegistration>(exception);
            }
        }
        /// <summary>
        ///     Get username suggestions
        /// </summary>
        /// <param name="name">Name</param>
        public async Task<IResult<InstaRegistrationSuggestionResponse>> GetUsernameSuggestionsAsync(string name)
        {
            return await GetUsernameSuggestions(name);
        }
        public async Task<IResult<InstaRegistrationSuggestionResponse>> GetUsernameSuggestions(string name, bool useNewIds = true)
        {
            try
            {
                if (string.IsNullOrEmpty(_deviceIdReg))
                    _deviceIdReg = ApiRequestMessage.GenerateDeviceId();
                if (useNewIds)
                {
                    _phoneIdReg = Guid.NewGuid().ToString();
                    _waterfallIdReg = Guid.NewGuid().ToString();
                    _guidReg = Guid.NewGuid().ToString();
                }
                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var postData = new Dictionary<string, string>
                {
                    {"name",            name},
                    {"_csrftoken",      csrftoken},
                    {"email",           ""}
                };
                if(useNewIds)
                {
                    postData.Add("phone_id", _phoneIdReg);
                    postData.Add("guid", _guidReg);
                    postData.Add("device_id", _deviceIdReg);
                    postData.Add("waterfall_id", _waterfallIdReg);
                }
                else
                {
                    postData.Add("phone_id", _deviceInfo.PhoneGuid.ToString());
                    postData.Add("guid", _deviceInfo.DeviceGuid.ToString());
                    postData.Add("device_id", _deviceInfo.DeviceId.ToString());
                    postData.Add("waterfall_id", _waterfallIdReg ?? Guid.NewGuid().ToString());
                }
                var instaUri = UriCreator.GetUsernameSuggestionsUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var o = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumber>(json);

                    return Result.Fail(o.Message?.Errors?[0], (InstaRegistrationSuggestionResponse)null);
                }

                var obj = JsonConvert.DeserializeObject<InstaRegistrationSuggestionResponse>(json);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaRegistrationSuggestionResponse>(exception);
            }
        }
        /// <summary>
        ///     Validate new account creation with phone number
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        /// <param name="verificationCode">Verification code</param>
        /// <param name="username">Username to set</param>
        /// <param name="password">Password to set</param>
        /// <param name="firstName">First name to set</param>
        public async Task<IResult<InstaAccountCreation>> ValidateNewAccountWithPhoneNumberAsync(string phoneNumber, string verificationCode, string username, string password, string firstName)
        {
            try
            {
                if (string.IsNullOrEmpty(_waterfallIdReg) || _signUpPhoneNumberInfo == null)
                    throw new ArgumentException("You should call SendSignUpSmsCodeAsync function first.");

                if(_signUpPhoneNumberInfo.GdprRequired)
                {
                    var acceptGdpr = await AcceptConsentRequiredAsync(null, phoneNumber);
                    if (!acceptGdpr.Succeeded)
                        return Result.Fail(acceptGdpr.Info.Message, (InstaAccountCreation)null);
                }
                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                
                var postData = new Dictionary<string, string>
                {
                    {"allow_contacts_sync",       "true"},
                    {"verification_code",         verificationCode},
                    {"sn_result",                 "API_ERROR:+null"},
                    {"phone_id",                  _deviceInfo.PhoneGuid.ToString()},
                    {"phone_number",              phoneNumber},
                    {"_csrftoken",                csrftoken},
                    {"username",                  username},
                    {"first_name",                firstName},
                    {"adid",                      Guid.NewGuid().ToString()},
                    {"guid",                      _deviceInfo.DeviceGuid.ToString()},
                    {"device_id",                 _deviceInfo.DeviceId},
                    {"sn_nonce",                  ""},
                    {"force_sign_up_code",        ""},
                    {"waterfall_id",              _waterfallIdReg},
                    {"qs_stamp",                  ""},
                    {"password",                  password},
                    {"has_sms_consent",           "true"},
                };
                if (_signUpPhoneNumberInfo.GdprRequired)
                    postData.Add("gdpr_s", "[0,2,0,null]");

                var instaUri = UriCreator.GetCreateValidatedUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var o = JsonConvert.DeserializeObject<InstaAccountCreationResponse>(json);

                    return Result.Fail(o.Errors?.Username?[0], (InstaAccountCreation)null);
                }

                var r = JsonConvert.DeserializeObject<InstaAccountCreationResponse>(json);
                if (r.ErrorType == "username_is_taken")
                    return Result.Fail(r.Errors?.Username?[0], (InstaAccountCreation)null);

                var obj = JsonConvert.DeserializeObject<InstaAccountCreation>(json);
                if (obj.AccountCreated && obj.CreatedUser != null)
                    ValidateUserAsync(obj.CreatedUser, csrftoken, true, password);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaAccountCreation>(exception);
            }
        }


        private async Task<IResult<object>> GetRegistrationStepsAsync()
        {
            try
            {
                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var postData = new Dictionary<string, string>
                {
                    {"fb_connected",            "false"},
                    {"seen_steps",            "[]"},
                    {"phone_id",        _phoneIdReg},
                    {"fb_installed",            "false"},
                    {"locale",            "en_US"},
                    {"timezone_offset",            "16200"},
                    {"network_type",            "WIFI-UNKNOWN"},
                    {"_csrftoken",      csrftoken},
                    {"guid",            _guidReg},
                    {"is_ci",            "false"},
                    {"android_id",       _deviceIdReg},
                    {"reg_flow_taken",           "phone"},
                    {"tos_accepted",    "false"},
                };
                var instaUri = UriCreator.GetOnboardingStepsUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var o = JsonConvert.DeserializeObject<InstaAccountRegistrationPhoneNumber>(json);

                    return Result.Fail(o.Message?.Errors?[0], (InstaRegistrationSuggestionResponse)null);
                }

                var obj = JsonConvert.DeserializeObject<InstaRegistrationSuggestionResponse>(json);
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaRegistrationSuggestionResponse>(exception);
            }
        }

        /// <summary>
        ///     Create a new instagram account [NEW FUNCTION, BUT NOT WORKING?!!!!!!!!!!]
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        /// <param name="email">Email</param>
        /// <param name="firstName">First name (optional)</param>
        /// <param name="delay">Delay between requests. null = 2.5 seconds</param>
        private async Task<IResult<InstaAccountCreation>> CreateNewAccountAsync(string username, string password, string email, string firstName = "", TimeSpan? delay = null)
        {
            var createResponse = new InstaAccountCreation();
            try
            {
                if (delay == null)
                    delay = TimeSpan.FromSeconds(2.5);

                var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                await firstResponse.Content.ReadAsStringAsync();
                var cookies =
                        _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                        .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                var checkEmail = await CheckEmail(email, false);
                if (!checkEmail.Succeeded)
                    return Result.Fail(checkEmail.Info.Message, (InstaAccountCreation)null);


                await Task.Delay((int)delay.Value.TotalMilliseconds);
                if (checkEmail.Value.GdprRequired)
                {
                    var acceptGdpr = await AcceptConsentRequiredAsync(email);
                    if (!acceptGdpr.Succeeded)
                        return Result.Fail(acceptGdpr.Info.Message, (InstaAccountCreation)null);
                }

                await Task.Delay((int)delay.Value.TotalMilliseconds);
                if (username.Length > 6)
                {
                    await GetUsernameSuggestions(username.Substring(0,4), false);
                    await Task.Delay(1000);
                    await GetUsernameSuggestions(username.Substring(0, 5), false);
                }
                else
                {
                    await GetUsernameSuggestions(username, false);
                    await Task.Delay(1000);
                    await GetUsernameSuggestions(username, false);
                }

                await Task.Delay((int)delay.Value.TotalMilliseconds);
                var postData = new Dictionary<string, string>
                {
                    {"allow_contacts_sync",       "true"},
                    {"sn_result",                 "API_ERROR:+null"},
                    {"phone_id",                  _deviceInfo.PhoneGuid.ToString()},
                    {"_csrftoken",                csrftoken},
                    {"username",                  username},
                    {"first_name",                firstName},
                    {"adid",                      Guid.NewGuid().ToString()},
                    {"guid",                      _deviceInfo.DeviceGuid.ToString()},
                    {"device_id",                 _deviceInfo.DeviceId.ToString()},
                    {"email",                     email},
                    {"sn_nonce",                  ""},
                    {"force_sign_up_code",        ""},
                    {"waterfall_id",              _waterfallIdReg ?? Guid.NewGuid().ToString()},
                    {"qs_stamp",                  ""},
                    {"password",                  password},
                };
                if (checkEmail.Value.GdprRequired)
                    postData.Add("gdpr_s", "[0,2,0,null]");

                var instaUri = UriCreator.GetCreateAccountUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaAccountCreation>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaAccountCreation>(json);
                //{"account_created": false, "errors": {"email": ["Another account is using iranramtin73jokar@live.com."], "username": ["This username isn't available. Please try another."]}, "allow_contacts_sync": true, "status": "ok", "error_type": "email_is_taken, username_is_taken"}
                //{"message": "feedback_required", "spam": true, "feedback_title": "Signup Error", "feedback_message": "Sorry! There\u2019s a problem signing you up right now. Please try again later. We restrict certain content and actions to protect our community. Tell us if you think we made a mistake.", "feedback_url": "repute/report_problem/instagram_signup/", "feedback_appeal_label": "Report problem", "feedback_ignore_label": "OK", "feedback_action": "report_problem", "status": "fail", "error_type": "signup_block"}

                if (obj.AccountCreated && obj.CreatedUser != null)
                    ValidateUserAsync(obj.CreatedUser, csrftoken, true, password);

                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaAccountCreation>(exception);
            }
        }

        /// <summary>
        ///     Create a new instagram account
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        /// <param name="email">Email</param>
        /// <param name="firstName">First name (optional)</param>
        /// <returns></returns>
        public async Task<IResult<InstaAccountCreation>> CreateNewAccountAsync(string username, string password, string email, string firstName)
        {
            InstaAccountCreation createResponse = new InstaAccountCreation();
            try
            {
                var _deviceIdReg = ApiRequestMessage.GenerateDeviceId();
                var _phoneIdReg = Guid.NewGuid().ToString();
                var _waterfallIdReg = Guid.NewGuid().ToString();
                var _guidReg = Guid.NewGuid().ToString();
                var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                await firstResponse.Content.ReadAsStringAsync();

                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;

                var postData = new Dictionary<string, string>
                {
                    {"allow_contacts_sync",       "true"},
                    {"sn_result",                 "API_ERROR:+null"},
                    {"phone_id",                  _phoneIdReg},
                    {"_csrftoken",                csrftoken},
                    {"username",                  username},
                    {"first_name",                firstName},
                    {"adid",                      Guid.NewGuid().ToString()},
                    {"guid",                      _guidReg},
                    {"device_id",                 _deviceIdReg},
                    {"email",                     email},
                    {"sn_nonce",                  ""},
                    {"force_sign_up_code",        ""},
                    {"waterfall_id",              _waterfallIdReg},
                    {"qs_stamp",                  ""},
                    {"password",                  password},
                };
                var instaUri = UriCreator.GetCreateAccountUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaAccountCreation>(response, json);
                var obj = JsonConvert.DeserializeObject<InstaAccountCreation>(json);
                if (obj.AccountCreated && obj.CreatedUser != null)
                    ValidateUserAsync(obj.CreatedUser, csrftoken, true, password);

                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaAccountCreation>(exception);
            }
        }
        /// <summary>
        ///     Accept consent require (for GDPR countries) 
        /// </summary>
        /// <param name="email"></param>
        /// <param name="phone"></param>
        /// <returns></returns>
        private async Task<IResult<bool>> AcceptConsentRequiredAsync(string email, string phone = null)
        {
            try
            {
                var delay = TimeSpan.FromSeconds(2);

                //{"message": "consent_required", "consent_data": {"headline": "Updates to Our Terms and Data Policy", "content": "We've updated our Terms and made some changes to our Data Policy. Please take a moment to review these changes and let us know that you agree to them.\n\nYou need to finish reviewing this information before you can use Instagram.", "button_text": "Review Now"}, "status": "fail"}
                await Task.Delay((int)delay.TotalMilliseconds);
                var instaUri = UriCreator.GetConsentNewUserFlowBeginsUri();
                var data = new JObject
                {
                    {"phone_id", _deviceInfo.PhoneGuid},
                    {"_csrftoken", _user.CsrfToken}
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);

                await Task.Delay((int)delay.TotalMilliseconds);

                instaUri = UriCreator.GetConsentNewUserFlowUri();
                data = new JObject
                {
                    {"phone_id", _deviceInfo.PhoneGuid},
                    {"gdpr_s", ""},
                    {"_csrftoken", _user.CsrfToken},
                    {"guid", _deviceInfo.DeviceGuid},
                    {"device_id", _deviceInfo.DeviceId}
                };
                if (email != null)
                    data.Add("email", email);
                else
                {
                    if (phone != null && !phone.StartsWith("+"))
                        phone = $"+{phone}";

                    if (phone == null)
                        phone = string.Empty;
                    data.Add("phone", phone);
                }

                request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);

                await Task.Delay((int)delay.TotalMilliseconds);

                data = new JObject
                {
                    {"current_screen_key", "age_consent_two_button"},
                    {"phone_id", _deviceInfo.PhoneGuid},
                    {"gdpr_s", "[0,0,0,null]"},
                    {"_csrftoken", _user.CsrfToken},
                    {"updates", "{\"age_consent_state\":\"2\"}"},
                    {"guid", _deviceInfo.DeviceGuid},
                    {"device_id", _deviceInfo.DeviceId}
                };
                request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);

                return Result.Success(true);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, false);
            }
        }
        #endregion Register new account with Phone number and email

        #region Authentication and challenge functions

        /// <summary>
        ///     Login using given credentials asynchronously
        /// </summary>
        /// <param name="isNewLogin"></param>
        /// <returns>
        ///     Success --> is succeed
        ///     TwoFactorRequired --> requires 2FA login.
        ///     BadPassword --> Password is wrong
        ///     InvalidUser --> User/phone number is wrong
        ///     Exception --> Something wrong happened
        /// </returns>
        public async Task<IResult<InstaLoginResult>> LoginAsync(bool isNewLogin = true)
        {
            ValidateUser();
            ValidateRequestMessage();
            try
            {
                if (isNewLogin)
                {
                    var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                    var html = await firstResponse.Content.ReadAsStringAsync();
                    _logger?.LogResponse(firstResponse);
                }
                var cookies =
                    _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                        .BaseAddress);
              
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var instaUri = UriCreator.GetLoginUri();
                var signature = string.Empty;
                var devid = string.Empty;
                if (isNewLogin)
                    signature = $"{_httpRequestProcessor.RequestMessage.GenerateSignature(_apiVersion,_apiVersion.SignatureKey, out devid)}.{_httpRequestProcessor.RequestMessage.GetMessageString()}";
                else
                    signature = $"{_httpRequestProcessor.RequestMessage.GenerateChallengeSignature(_apiVersion, _apiVersion.SignatureKey, csrftoken, out devid)}.{_httpRequestProcessor.RequestMessage.GetChallengeMessageString(csrftoken)}";
                _deviceInfo.DeviceId = devid;
                var fields = new Dictionary<string, string>
                {
                    {InstaApiConstants.HEADER_IG_SIGNATURE, signature},
                    {InstaApiConstants.HEADER_IG_SIGNATURE_KEY_VERSION, InstaApiConstants.IG_SIGNATURE_KEY_VERSION}
                };
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Headers.Add("Host", "i.instagram.com");
                request.Content = new FormUrlEncodedContent(fields);
                request.Properties.Add(InstaApiConstants.HEADER_IG_SIGNATURE, signature);
                request.Properties.Add(InstaApiConstants.HEADER_IG_SIGNATURE_KEY_VERSION, InstaApiConstants.IG_SIGNATURE_KEY_VERSION);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var loginFailReason = JsonConvert.DeserializeObject<InstaLoginBaseResponse>(json);

                    if (loginFailReason.InvalidCredentials)
                        return Result.Fail("Invalid Credentials",
                            loginFailReason.ErrorType == "bad_password"
                                ? InstaLoginResult.BadPassword
                                : InstaLoginResult.InvalidUser);
                    if (loginFailReason.TwoFactorRequired)
                    {
                        _twoFactorInfo = loginFailReason.TwoFactorLoginInfo;
                        //2FA is required!
                        return Result.Fail("Two Factor Authentication is required", InstaLoginResult.TwoFactorRequired);
                    }
                    if (loginFailReason.ErrorType == "checkpoint_challenge_required"
                       /* || !string.IsNullOrEmpty(loginFailReason.Message) && loginFailReason.Message == "challenge_required"*/)
                    {
                        _challengeinfo = loginFailReason.Challenge;

                        return Result.Fail("Challenge is required", InstaLoginResult.ChallengeRequired);
                    }
                    if (loginFailReason.ErrorType == "rate_limit_error")
                    {
                        return Result.Fail("Please wait a few minutes before you try again.", InstaLoginResult.LimitError);
                    }
                    if (loginFailReason.ErrorType == "inactive user" || loginFailReason.ErrorType == "inactive_user")
                    {
                        return Result.Fail($"{loginFailReason.Message}\r\nHelp url: {loginFailReason.HelpUrl}", InstaLoginResult.InactiveUser);
                    }
                    return Result.UnExpectedResponse<InstaLoginResult>(response, json);
                }
                var loginInfo = JsonConvert.DeserializeObject<InstaLoginResponse>(json);
                IsUserAuthenticated = loginInfo.User?.UserName.ToLower() == _user.UserName.ToLower();
                var converter = ConvertersFabric.Instance.GetUserShortConverter(loginInfo.User);
                _user.LoggedInUser = converter.Convert();
                _user.RankToken = $"{_user.LoggedInUser.Pk}_{_httpRequestProcessor.RequestMessage.PhoneId}";
                if(string.IsNullOrEmpty(_user.CsrfToken))
                {
                    cookies =
                      _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                          .BaseAddress);
                    _user.CsrfToken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                }
                return Result.Success(InstaLoginResult.Success);
            }
            catch (Exception exception)
            {
                LogException(exception);
                return Result.Fail(exception, InstaLoginResult.Exception);
            }
            finally
            {
                InvalidateProcessors();
            }
        }

        /// <summary>
        ///     2-Factor Authentication Login using a verification code
        ///     Before call this method, please run LoginAsync first.
        /// </summary>
        /// <param name="verificationCode">Verification Code sent to your phone number</param>
        /// <returns>
        ///     Success --> is succeed
        ///     InvalidCode --> The code is invalid
        ///     CodeExpired --> The code is expired, please request a new one.
        ///     Exception --> Something wrong happened
        /// </returns>
        public async Task<IResult<InstaLoginTwoFactorResult>> TwoFactorLoginAsync(string verificationCode)
        {
            if (_twoFactorInfo == null)
                return Result.Fail<InstaLoginTwoFactorResult>("Run LoginAsync first");

            try
            {
                var twoFactorRequestMessage = new ApiTwoFactorRequestMessage(verificationCode,
                    _httpRequestProcessor.RequestMessage.Username,
                    _httpRequestProcessor.RequestMessage.DeviceId,
                    _twoFactorInfo.TwoFactorIdentifier);

                var instaUri = UriCreator.GetTwoFactorLoginUri();
                var signature =
                    $"{twoFactorRequestMessage.GenerateSignature(_apiVersion, _apiVersion.SignatureKey)}.{twoFactorRequestMessage.GetMessageString()}";
                var fields = new Dictionary<string, string>
                {
                    {InstaApiConstants.HEADER_IG_SIGNATURE, signature},
                    {
                        InstaApiConstants.HEADER_IG_SIGNATURE_KEY_VERSION,
                        InstaApiConstants.IG_SIGNATURE_KEY_VERSION
                    }
                };
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = new FormUrlEncodedContent(fields);
                request.Properties.Add(InstaApiConstants.HEADER_IG_SIGNATURE, signature);
                request.Properties.Add(InstaApiConstants.HEADER_IG_SIGNATURE_KEY_VERSION,
                    InstaApiConstants.IG_SIGNATURE_KEY_VERSION);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var loginInfo =
                        JsonConvert.DeserializeObject<InstaLoginResponse>(json);
                    IsUserAuthenticated = IsUserAuthenticated =
                        loginInfo.User != null && loginInfo.User.UserName.ToLower() == _user.UserName.ToLower();
                    var converter = ConvertersFabric.Instance.GetUserShortConverter(loginInfo.User);
                    _user.LoggedInUser = converter.Convert();
                    _user.RankToken = $"{_user.LoggedInUser.Pk}_{_httpRequestProcessor.RequestMessage.PhoneId}";

                    return Result.Success(InstaLoginTwoFactorResult.Success);
                }

                var loginFailReason = JsonConvert.DeserializeObject<InstaLoginTwoFactorBaseResponse>(json);

                if (loginFailReason.ErrorType == "sms_code_validation_code_invalid")
                    return Result.Fail("Please check the security code.", InstaLoginTwoFactorResult.InvalidCode);
                return Result.Fail("This code is no longer valid, please, call LoginAsync again to request a new one",
                    InstaLoginTwoFactorResult.CodeExpired);
            }
            catch (Exception exception)
            {
                LogException(exception);
                return Result.Fail(exception, InstaLoginTwoFactorResult.Exception);
            }
        }

        /// <summary>
        ///     Get Two Factor Authentication details
        /// </summary>
        /// <returns>
        ///     An instance of TwoFactorInfo if success.
        ///     A null reference if not success; in this case, do LoginAsync first and check if Two Factor Authentication is
        ///     required, if not, don't run this method
        /// </returns>
        public async Task<IResult<InstaTwoFactorLoginInfo>> GetTwoFactorInfoAsync()
        {
            return await Task.Run(() =>
                _twoFactorInfo != null
                    ? Result.Success(_twoFactorInfo)
                    : Result.Fail<InstaTwoFactorLoginInfo>("No Two Factor info available."));
        }

        /// <summary>
        ///     Logout from instagram asynchronously
        /// </summary>
        /// <returns>
        ///     True if logged out without errors
        /// </returns>
        public async Task<IResult<bool>> LogoutAsync()
        {
            ValidateUser();
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetLogoutUri();
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK) return Result.UnExpectedResponse<bool>(response, json);
                var logoutInfo = JsonConvert.DeserializeObject<BaseStatusResponse>(json);
                if (logoutInfo.Status == "ok")
                    IsUserAuthenticated = false;
                return Result.Success(!IsUserAuthenticated);
            }
            catch (Exception exception)
            {
                LogException(exception);
                return Result.Fail(exception, false);
            }
        }

        /// <summary>
        ///     Send recovery code by Username
        /// </summary>
        /// <param name="username">Username</param>
        public async Task<IResult<InstaRecovery>> SendRecoveryByUsernameAsync(string username)
        {
            return await SendRecoveryByEmailAsync(username);
        }

        /// <summary>
        ///     Send recovery code by Email
        /// </summary>
        /// <param name="email">Email Address</param>
        public async Task<IResult<InstaRecovery>> SendRecoveryByEmailAsync(string email)
        {
            try
            {
                var token = "";
                if (!string.IsNullOrEmpty(_user.CsrfToken))
                    token = _user.CsrfToken;
                else
                {
                    var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                    var cookies =
                        _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                            .BaseAddress);
                    _logger?.LogResponse(firstResponse);
                    token = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                }

                var postData = new JObject
                {
                    {"query", email },
                    {"adid", _deviceInfo.GoogleAdId },
                    {"device_id",  ApiRequestMessage.GenerateDeviceId()},
                    {"guid",  _deviceInfo.DeviceGuid.ToString()},
                    {"_csrftoken", token },
                };

                var instaUri = UriCreator.GetAccountRecoveryEmailUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);

                var response = await _httpRequestProcessor.SendAsync(request);

                var result = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var error = JsonConvert.DeserializeObject<MessageErrorsResponseRecoveryEmail>(result);
                    return Result.Fail<InstaRecovery>(error.Message);
                }

                return Result.Success(JsonConvert.DeserializeObject<InstaRecovery>(result));
            }
            catch (Exception exception)
            {
                return Result.Fail<InstaRecovery>(exception);
            }
        }

        /// <summary>
        ///     Send recovery code by Phone
        /// </summary>
        /// <param name="phone">Phone Number</param>
        public async Task<IResult<InstaRecovery>> SendRecoveryByPhoneAsync(string phone)
        {
            try
            {
                var token = "";
                if (!string.IsNullOrEmpty(_user.CsrfToken))
                    token = _user.CsrfToken;
                else
                {
                    var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                    var cookies =
                        _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                            .BaseAddress);
                    _logger?.LogResponse(firstResponse);
                    token = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                }

                var postData = new JObject
                {
                    {"query",  phone},
                    {"_csrftoken",  _user.CsrfToken},
                };

                var instaUri = UriCreator.GetAccountRecoverPhoneUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);

                var response = await _httpRequestProcessor.SendAsync(request);
                var result = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var error = JsonConvert.DeserializeObject<BadStatusErrorsResponse>(result);
                    var errors = "";
                    error.Message.Errors.ForEach(errorContent => errors += errorContent + "\n");
                    return Result.Fail<InstaRecovery>(errors);
                }

                if (result.Contains("errors"))
                {
                    var error = JsonConvert.DeserializeObject<BadStatusErrorsResponseRecovery>(result);
                    var errors = "";
                    error.Phone_number.Errors.ForEach(errorContent => errors += errorContent + "\n");

                    return Result.Fail<InstaRecovery>(errors);
                }
                return Result.Success(JsonConvert.DeserializeObject<InstaRecovery>(result));
            }
            catch (Exception exception)
            {
                return Result.Fail<InstaRecovery>(exception);
            }
        }


        /// <summary>
        ///    Send Two Factor Login SMS Again
        /// </summary>
        public async Task<IResult<TwoFactorLoginSMS>> SendTwoFactorLoginSMSAsync()
        {
            try
            {
                if (_twoFactorInfo == null)
                    return Result.Fail<TwoFactorLoginSMS>("Run LoginAsync first");

                var postData = new Dictionary<string, string>
                {
                    { "two_factor_identifier",  _twoFactorInfo.TwoFactorIdentifier },
                    { "username",    _httpRequestProcessor.RequestMessage.Username},
                    { "device_id",   _httpRequestProcessor.RequestMessage.DeviceId},
                    { "guid",        _deviceInfo.DeviceGuid.ToString()},
                    { "_csrftoken",    _user.CsrfToken }
                };

                var instaUri = UriCreator.GetAccount2FALoginAgainUri();
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, postData);
                var response = await _httpRequestProcessor.SendAsync(request);
                var result = await response.Content.ReadAsStringAsync();

                var T = JsonConvert.DeserializeObject<TwoFactorLoginSMS>(result);
                if (!string.IsNullOrEmpty(T.TwoFactorInfo.TwoFactorIdentifier))
                    _twoFactorInfo.TwoFactorIdentifier = T.TwoFactorInfo.TwoFactorIdentifier;
                return Result.Success(T);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<TwoFactorLoginSMS>(exception);
            }
        }
        
        #region Challenge part
        /// <summary>
        ///     Get challenge require (checkpoint required) options
        /// </summary>
        public async Task<IResult<InstaChallengeRequireVerifyMethod>> GetChallengeRequireVerifyMethodAsync()
        {
            if (_challengeinfo == null)
                return Result.Fail("challenge require info is empty.\r\ntry to call LoginAsync function first.", (InstaChallengeRequireVerifyMethod)null);

            try
            {
                var instaUri = UriCreator.GetChallengeRequireFirstUri(_challengeinfo.ApiPath, _deviceInfo.DeviceGuid.ToString(), _deviceInfo.DeviceId);
                var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var msg = "";
                    try
                    {
                        var j = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyMethod>(json);
                        msg = j.Message;
                    }
                    catch { }
                    return Result.UnExpectedResponse<InstaChallengeRequireVerifyMethod>(response, json);
                }

                var obj = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyMethod>(json);
                return Result.Success(obj);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, (InstaChallengeRequireVerifyMethod)null);
            }
        }
        /// <summary>
        ///     Reset challenge require (checkpoint required) method
        /// </summary>
        public async Task<IResult<InstaChallengeRequireVerifyMethod>> ResetChallengeRequireVerifyMethodAsync()
        {
            if (_challengeinfo == null)
                return Result.Fail("challenge require info is empty.\r\ntry to call LoginAsync function first.", (InstaChallengeRequireVerifyMethod)null);

            try
            {
                var instaUri = UriCreator.GetResetChallengeRequireUri(_challengeinfo.ApiPath);
                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"guid", _deviceInfo.DeviceGuid.ToString()},
                    {"device_id", _deviceInfo.DeviceId},
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var msg = "";
                    try
                    {
                        var j = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyMethod>(json);
                        msg = j.Message;
                    }
                    catch { }
                    return Result.UnExpectedResponse<InstaChallengeRequireVerifyMethod>(response, json);
                }

                var obj = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyMethod>(json);
                return Result.Success(obj);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, (InstaChallengeRequireVerifyMethod)null);
            }
        }
        /// <summary>
        ///     Request verification code sms for challenge require (checkpoint required)
        /// </summary>
        public async Task<IResult<InstaChallengeRequireSMSVerify>> RequestVerifyCodeToSMSForChallengeRequireAsync()
        {
            return await RequestVerifyCodeToSMSForChallengeRequire();
        }
        /// <summary>
        ///     Submit phone number for challenge require (checkpoint required)
        ///     <para>Note: This only needs , when you calling <see cref="IInstaApi.GetChallengeRequireVerifyMethodAsync"/> or
        ///     <see cref="IInstaApi.ResetChallengeRequireVerifyMethodAsync"/> and
        ///     <see cref="InstaChallengeRequireVerifyMethod.SubmitPhoneRequired"/> property is true.</para>
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        public async Task<IResult<InstaChallengeRequireSMSVerify>> SubmitPhoneNumberForChallengeRequireAsync(string phoneNumber)
        {
            return await RequestVerifyCodeToSMSForChallengeRequire(phoneNumber);
        }

        private async Task<IResult<InstaChallengeRequireSMSVerify>> RequestVerifyCodeToSMSForChallengeRequire(string phoneNumber = null)
        {
            if (_challengeinfo == null)
                return Result.Fail("challenge require info is empty.\r\ntry to call LoginAsync function first.", (InstaChallengeRequireSMSVerify)null);

            try
            {
                var instaUri = UriCreator.GetChallengeRequireUri(_challengeinfo.ApiPath);

                var data = new JObject
                {
                    {"_csrftoken", _user.CsrfToken},
                    {"guid", _deviceInfo.DeviceGuid.ToString()},
                    {"device_id", _deviceInfo.DeviceId},
                };
                if (!string.IsNullOrEmpty(phoneNumber))
                    data.Add("phone_number", phoneNumber);
                else
                    data.Add("choice", "0");

                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Add("Host", "i.instagram.com");
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var msg = "";
                    try
                    {
                        var j = JsonConvert.DeserializeObject<InstaChallengeRequireSMSVerify>(json);
                        msg = j.Message;
                    }
                    catch { }
                    return Result.Fail(msg, (InstaChallengeRequireSMSVerify)null);
                }

                var obj = JsonConvert.DeserializeObject<InstaChallengeRequireSMSVerify>(json);
                return Result.Success(obj);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, (InstaChallengeRequireSMSVerify)null);
            }
        }
        /// <summary>
        ///     Request verification code email for challenge require (checkpoint required)
        /// </summary>
        public async Task<IResult<InstaChallengeRequireEmailVerify>> RequestVerifyCodeToEmailForChallengeRequireAsync()
        {
            if (_challengeinfo == null)
                return Result.Fail("challenge require info is empty.\r\ntry to call LoginAsync function first.", (InstaChallengeRequireEmailVerify)null);

            try
            {
                var instaUri = UriCreator.GetChallengeRequireUri(_challengeinfo.ApiPath);

                var data = new JObject
                {
                    {"choice", "1"},
                    {"_csrftoken", _user.CsrfToken},
                    {"guid", _deviceInfo.DeviceGuid.ToString()},
                    {"device_id", _deviceInfo.DeviceId},
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Add("Host", "i.instagram.com");
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var msg = "";
                    try
                    {
                        var j = JsonConvert.DeserializeObject<InstaChallengeRequireEmailVerify>(json);
                        msg = j.Message;
                    }
                    catch { }
                    return Result.Fail(msg, (InstaChallengeRequireEmailVerify)null);
                }

                var obj = JsonConvert.DeserializeObject<InstaChallengeRequireEmailVerify>(json);
                return Result.Success(obj);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex, (InstaChallengeRequireEmailVerify)null);
            }
        }
        /// <summary>
        ///     Verify verification code for challenge require (checkpoint required)
        /// </summary>
        /// <param name="verifyCode">Verification code</param>
        public async Task<IResult<InstaLoginResult>> VerifyCodeForChallengeRequireAsync(string verifyCode)
        {
            if (_challengeinfo == null)
                return Result.Fail("challenge require info is empty.\r\ntry to call LoginAsync function first.", InstaLoginResult.Exception);

            if (verifyCode.Length != 6)
                return Result.Fail("Verify code must be an 6 digit number.", InstaLoginResult.Exception);

            try
            {
                var cookies =
            _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                _user.CsrfToken = csrftoken;
                var instaUri = UriCreator.GetChallengeRequireUri(_challengeinfo.ApiPath);

                var data = new JObject
                {
                    {"security_code", verifyCode},
                    {"_csrftoken", _user.CsrfToken},
                    {"guid", _deviceInfo.DeviceGuid.ToString()},
                    {"device_id", _deviceInfo.DeviceId},
                };
                var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Add("Host", "i.instagram.com");
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var msg = "";
                    try
                    {
                        var j = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyCode>(json);
                        msg = j.Message;
                    }
                    catch { }
                    return Result.UnExpectedResponse<InstaLoginResult>(response, msg + "\t"+ json);
                }

                var obj = JsonConvert.DeserializeObject<InstaChallengeRequireVerifyCode>(json);
                if (obj != null)
                {
                    if (obj.LoggedInUser != null)
                    {
                        ValidateUserAsync(obj.LoggedInUser, csrftoken);
                        await Task.Delay(3000);
                        await _messagingProcessor.GetDirectInboxAsync();
                        await _feedProcessor.GetRecentActivityFeedAsync(PaginationParameters.MaxPagesToLoad(1));

                        return Result.Success(InstaLoginResult.Success);
                    }

                    if (!string.IsNullOrEmpty(obj.Action))
                    {
                        // we should wait at least 15 seconds and then trying to login again
                        await Task.Delay(15000);
                        return await LoginAsync(false);
                    }
                }
                return Result.UnExpectedResponse<InstaLoginResult>(response, json);
            }
            catch (Exception ex)
            {
                LogException(ex);
                return Result.Fail(ex, InstaLoginResult.Exception);
            }
        }
        #endregion Challenge part

        /// <summary>
        ///     Set cookie and html document to verify login information.
        /// </summary>
        /// <param name="htmlDocument">Html document source</param>
        /// <param name="cookies">Cookies from webview or webbrowser control</param>
        /// <returns>True if logged in, False if not</returns>
        public async Task<IResult<bool>> SetCookiesAndHtmlForFacebookLoginAsync(string htmlDocument, string cookie, bool facebookLogin = true)
        {
            if (!string.IsNullOrEmpty(cookie) && !string.IsNullOrEmpty(htmlDocument))
            {
                try
                {
                    var start = "<script type=\"text/javascript\">window._sharedData";
                    var end = ";</script>";

                    var str = htmlDocument.Substring(htmlDocument.IndexOf(start) + start.Length);
                    str = str.Substring(0, str.IndexOf(end));
                    str = str.Substring(str.IndexOf("=") + 2);
                    var o = JsonConvert.DeserializeObject<InstaWebBrowserResponse>(str);
                    return await SetCookiesAndHtmlForFacebookLogin(o, cookie, facebookLogin);
                }
                catch (Exception ex)
                {
                    return Result.Fail(ex.Message, false);
                }
            }
            return Result.Fail("", false);
        }
        /// <summary>
        ///     Set cookie and web browser response object to verify login information.
        /// </summary>
        /// <param name="webBrowserResponse">Web browser response object</param>
        /// <param name="cookies">Cookies from webview or webbrowser control</param>
        /// <returns>True if logged in, False if not</returns>
        public async Task<IResult<bool>> SetCookiesAndHtmlForFacebookLogin(InstaWebBrowserResponse webBrowserResponse, string cookie, bool facebookLogin = true)
        {
            if(webBrowserResponse == null)
                return Result.Fail("", false);
            if(webBrowserResponse.Config == null)
                return Result.Fail("", false);
            if(webBrowserResponse.Config.Viewer == null)
                return Result.Fail("", false);

            if (!string.IsNullOrEmpty(cookie))
            {
                try
                {
                    var uri = new Uri(InstaApiConstants.INSTAGRAM_URL);
                    //if (cookie.Contains("urlgen"))
                    //{
                    //    var removeStart = "urlgen=";
                    //    var removeEnd = ";";
                    //    var t = cookie.Substring(cookie.IndexOf(removeStart) + 0);
                    //    t = t.Substring(0, t.IndexOf(removeEnd) + 2);
                    //    cookie = cookie.Replace(t, "");
                    //}
                    cookie = cookie.Replace(';', ',');
                    _httpRequestProcessor.HttpHandler.CookieContainer.SetCookies(uri, cookie);

                    var user = new InstaUserShort
                    {
                        Pk = long.Parse(webBrowserResponse.Config.Viewer.Id),
                        UserName = _user.UserName,
                        ProfilePictureId = "unknown",
                        FullName = webBrowserResponse.Config.Viewer.FullName,
                        ProfilePicture = webBrowserResponse.Config.Viewer.ProfilePicUrl
                    };
                    _user.LoggedInUser = user;
                    _user.CsrfToken = webBrowserResponse.Config.CsrfToken;
                    _user.RankToken = $"{webBrowserResponse.Config.Viewer.Id}_{_httpRequestProcessor.RequestMessage.PhoneId}";
                    IsUserAuthenticated = true;
                    if (facebookLogin)
                    {
                        try
                        {
                            var instaUri = UriCreator.GetFacebookSignUpUri();
                            var data = new JObject
                            {
                                {"dryrun", "true"},
                                {"phone_id", _deviceInfo.DeviceGuid.ToString()},
                                {"_csrftoken", _user.CsrfToken},
                                {"adid", Guid.NewGuid().ToString()},
                                {"guid", Guid.NewGuid().ToString()},
                                {"device_id", ApiRequestMessage.GenerateDeviceId()},
                                {"waterfall_id", Guid.NewGuid().ToString()},
                                {"fb_access_token", InstaApiConstants.FB_ACCESS_TOKEN},
                            };
                            var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                            request.Headers.Add("Host", "i.instagram.com");
                            var response = await _httpRequestProcessor.SendAsync(request);
                            var json = await response.Content.ReadAsStringAsync();
                            var obj = JsonConvert.DeserializeObject<InstaFacebookLoginResponse>(json);
                            _user.FacebookUserId = obj.FbUserId;
                        }
                        catch(Exception)
                        {
                        }
                        InvalidateProcessors();
                    }
                    return Result.Success(true);
                }
                catch (Exception ex)
                {
                    return Result.Fail(ex.Message, false);
                }
            }
            return Result.Fail("", false);
        }

        /* NEEDS A LOT OF WORK
        private async Task<IResult<bool>> SetCookiesAndHtmlForFacebookLogin(InstaFacebookAccountInfo facebookAccount, string cookiesContainer)
        {
            if (facebookAccount == null)
                return Result.Fail("", false);

            if (string.IsNullOrEmpty(facebookAccount.Token) || string.IsNullOrEmpty(facebookAccount.UserId))
                return Result.Fail("", false);
            try
            {
                //var firstResponse = await _httpRequestProcessor.GetAsync(_httpRequestProcessor.Client.BaseAddress);
                //await firstResponse.Content.ReadAsStringAsync();
                //_logger?.LogResponse(firstResponse);

                var cookies =
                _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                    .BaseAddress);
                var csrftoken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                var uri = new Uri(InstaApiConstants.INSTAGRAM_URL);

                cookiesContainer = cookiesContainer.Replace(';', ',');
                _httpRequestProcessor.HttpHandler.CookieContainer.SetCookies(uri, cookiesContainer);
                //var user = new InstaUserShort
                //{
                //    Pk = long.Parse(webBrowserResponse.Config.Viewer.Id),
                //    UserName = _user.UserName,
                //    ProfilePictureId = "unknown",
                //    FullName = webBrowserResponse.Config.Viewer.FullName,
                //    ProfilePicture = webBrowserResponse.Config.Viewer.ProfilePicUrl
                //};
                //_user.LoggedInUser = user;
                //_user.CsrfToken = webBrowserResponse.Config.CsrfToken;
                //_user.RankToken = $"{webBrowserResponse.Config.Viewer.Id}_{_httpRequestProcessor.RequestMessage.PhoneId}";

                //IsUserAuthenticated = true;

                try
                {

                    //var instaUri = new Uri(string.Format(InstaApiConstants.FACEBOOK_TOKEN1,facebookAccount.Token));
                    //var request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                    //var response = await _httpRequestProcessor.SendAsync(request);
                    //var json = await response.Content.ReadAsStringAsync();

                    //instaUri = new Uri(string.Format(InstaApiConstants.FACEBOOK_TOKEN_PICTURE));
                    //request = _httpHelper.GetDefaultRequest(HttpMethod.Get, instaUri, _deviceInfo);
                    //response = await _httpRequestProcessor.SendAsync(request);
                    //json = await response.Content.ReadAsStringAsync();



                    var instaUri = UriCreator.GetFacebookSignUpUri();
                    //{
                    //	"dryrun": "true",
                    //	"phone_id": "422c0750-4362-480f-a6ed-20271d510b4f",
                    //	"_csrftoken": "ggv0OmlT6Vv3NDJyw3vWfKFyrKTwyG7z",
                    //	"_uid": "8651542203",
                    //	"adid": "4aaa31c1-e48c-49b5-9dd7-262b2156a86d",
                    //	"guid": "6324ecb2-e663-4dc8-a3a1-289c699cc876",
                    //	"device_id": "android-70d6ba15a3d76520",
                    //	"_uuid": "6324ecb2-e663-4dc8-a3a1-289c699cc876",
                    //	"waterfall_id": "6edc16bb-94e4-4768-8684-0023ec9b300e",
                    //	"fb_access_token": "EAABwzLixnjYBAJk6hbz17jun4WZAC9CSR9rb0Nl4g2fnXu4D4bgYLXBRnNrxgi93DRX5H4yg8HAYlYaTnOEBwcEmdVWD9zkn9uvsoNaEaO40FWqmqQ9heom8XVEZB6gp4G8AYfD0B0lMpY8mOit1FXFVZAqKGH0K5rbt33UlwZDZD"
                    //}
                    var data = new JObject
                            {
                                {"dryrun", "true"},
                                {"phone_id", _deviceInfo.DeviceGuid.ToString()},
                                {"_csrftoken", _user.CsrfToken},
                                {"adid", Guid.NewGuid().ToString()},
                                {"guid", Guid.NewGuid().ToString()},
                                {"device_id", ApiRequestMessage.GenerateDeviceId()},
                                {"waterfall_id", Guid.NewGuid().ToString()},
                                {"fb_access_token", InstaApiConstants.FB_ACCESS_TOKEN},
                            };
                    var request = _httpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                    var response = await _httpRequestProcessor.SendAsync(request);
                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JsonConvert.DeserializeObject<InstaFacebookLoginResponse>(json);
                    var loginInfo = JsonConvert.DeserializeObject<InstaLoginResponse>(json);
                    IsUserAuthenticated = loginInfo.User?.UserName.ToLower() == _user.UserName.ToLower();
                    var converter = ConvertersFabric.Instance.GetUserShortConverter(loginInfo.User);
                    _user.LoggedInUser = converter.Convert();
                    _user.RankToken = $"{_user.LoggedInUser.Pk}_{_httpRequestProcessor.RequestMessage.PhoneId}";
                    _user.CsrfToken = csrftoken;
                    _user.FacebookUserId = obj.FbUserId;
                }
                catch (Exception)
                {
                }
                InvalidateProcessors();

                return Result.Success(true);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.Message, false);
            }

        }
        */

        #endregion Authentication and challenge functions

        #region Other public functions

        /// <summary>
        ///     Gets current device
        /// </summary>
        public AndroidDevice GetCurrentDevice()
        {
            return _deviceInfo;
        }
        /// <summary>
        ///     Gets logged in user
        /// </summary>
        public UserSessionData GetLoggedUser()
        {
            return _user;
        }
        /// <summary>
        ///     Get currently logged in user info asynchronously
        /// </summary>
        /// <returns>
        ///     <see cref="T:InstagramApiSharp.Classes.Models.InstaCurrentUser" />
        /// </returns>
        public async Task<IResult<InstaCurrentUser>> GetCurrentUserAsync()
        {
            ValidateUser();
            ValidateLoggedIn();
            return await _userProcessor.GetCurrentUserAsync();
        }
        /// <summary>
        ///     Get Accept Language
        /// </summary>
        public string GetAcceptLanguage()
        {
            try
            {
                return InstaApiConstants.ACCEPT_LANGUAGE;
            }
            catch (Exception exception)
            {
                return Result.Fail<string>(exception).Value;
            }
        }
        /// <summary>
        ///     Set delay between requests. Useful when API supposed to be used for mass-bombing.
        /// </summary>
        /// <param name="delay">Timespan delay</param>
        public void SetRequestDelay(IRequestDelay delay)
        {
            if (delay == null)
                delay = RequestDelay.Empty();
            _delay = delay;
            _httpRequestProcessor.Delay = _delay;

        }
        /// <summary>
        ///     Set instagram api version (for user agent version)
        /// </summary>
        /// <param name="apiVersion">Api version</param>
        public void SetApiVersion(InstaApiVersionType apiVersion)
        {
            _apiVersionType = apiVersion;
        }
        /// <summary>
        ///     Set custom android device.
        ///     <para>Note 1: If you want to use this method, you should call it before you calling <seealso cref="IInstaApi.LoadStateDataFromStream(Stream)"/> or <seealso cref="IInstaApi.LoadStateDataFromString(string)"/></para>
        ///     <para>Note 2: this is optional, if you didn't set this, InstagramApiSharp will choose random device.</para>
        /// </summary>
        /// <param name="device">Android device</param>
        public void SetDevice(AndroidDevice device)
        {
            IsCustomDeviceSet = false;
            if (device == null)
                return;
            _deviceInfo = device;
            IsCustomDeviceSet = true;
        }
        /// <summary>
        ///     Set Accept Language
        /// </summary>
        /// <param name="languageCodeAndCountryCode">Language Code and Country Code. For example:
        /// <para>en-US for united states</para>
        /// <para>fa-IR for IRAN</para>
        /// </param>
        public bool SetAcceptLanguage(string languageCodeAndCountryCode)
        {
            try
            {
                InstaApiConstants.ACCEPT_LANGUAGE = languageCodeAndCountryCode;
                return true;
            }
            catch (Exception exception)
            {
                return Result.Fail<bool>(exception).Value;
            }
        }
        #endregion Other public functions

        #region State data

        /// <summary>
        ///     Get current state info as Memory stream
        /// </summary>
        /// <returns>
        ///     State data
        /// </returns>
        public Stream GetStateDataAsStream()
        {

            var Cookies = _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(new Uri(InstaApiConstants.INSTAGRAM_URL));
            var RawCookiesList = new List<Cookie>();
            foreach (Cookie cookie in Cookies)
            {
                RawCookiesList.Add(cookie);
            }


            var state = new StateData
            {
                DeviceInfo = _deviceInfo,
                IsAuthenticated = IsUserAuthenticated,
                UserSession = _user,
                Cookies = _httpRequestProcessor.HttpHandler.CookieContainer,
                RawCookies = RawCookiesList,
                InstaApiVersion = _apiVersionType
            };
            return SerializationHelper.SerializeToStream(state);
        }
        /// <summary>
        ///     Get current state info as Json string
        /// </summary>
        /// <returns>
        ///     State data
        /// </returns>
        public string GetStateDataAsString()
        {

            var Cookies = _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(new Uri(InstaApiConstants.INSTAGRAM_URL));
            var RawCookiesList = new List<Cookie>();
            foreach (Cookie cookie in Cookies)
            {
                RawCookiesList.Add(cookie);
            }

            var state = new StateData
            {
                DeviceInfo = _deviceInfo,
                IsAuthenticated = IsUserAuthenticated,
                UserSession = _user,
                Cookies = _httpRequestProcessor.HttpHandler.CookieContainer,
                RawCookies = RawCookiesList,
                InstaApiVersion = _apiVersionType
            };
            return SerializationHelper.SerializeToString(state);
        }
        /// <summary>
        ///     Get current state info as Memory stream asynchronously
        /// </summary>
        /// <returns>
        ///     State data
        /// </returns>
        public async Task<Stream> GetStateDataAsStreamAsync()
        {
            return await Task<Stream>.Factory.StartNew(() =>
            {
                var state = GetStateDataAsStream();
                Task.Delay(1000);
                return state;
            });
        }
        /// <summary>
        ///     Get current state info as Json string asynchronously
        /// </summary>
        /// <returns>
        ///     State data
        /// </returns>
        public async Task<string> GetStateDataAsStringAsync()
        {
            return await Task<string>.Factory.StartNew(() =>
            {
                var state = GetStateDataAsString();
                Task.Delay(1000);
                return state;
            });
        }
        /// <summary>
        ///     Loads the state data from stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        public void LoadStateDataFromStream(Stream stream)
        {
            var data = SerializationHelper.DeserializeFromStream<StateData>(stream);
            if (!IsCustomDeviceSet)
                _deviceInfo = data.DeviceInfo;
            _user = data.UserSession;
            
            _httpRequestProcessor.RequestMessage.Username = data.UserSession.UserName;
            _httpRequestProcessor.RequestMessage.Password = data.UserSession.Password;
            
            _httpRequestProcessor.RequestMessage.DeviceId = data.DeviceInfo.DeviceId;
            _httpRequestProcessor.RequestMessage.PhoneId = data.DeviceInfo.PhoneGuid.ToString();
            _httpRequestProcessor.RequestMessage.Guid = data.DeviceInfo.DeviceGuid;
            _httpRequestProcessor.RequestMessage.AdId = data.DeviceInfo.AdId.ToString();

            foreach (var cookie in data.RawCookies)
            {
                _httpRequestProcessor.HttpHandler.CookieContainer.Add(new Uri(InstaApiConstants.INSTAGRAM_URL), cookie);
            }

            if (data.InstaApiVersion == null)
                data.InstaApiVersion = InstaApiVersionType.Version44;
            _apiVersionType = data.InstaApiVersion.Value;
            _apiVersion = InstaApiVersionList.GetApiVersionList().GetApiVersion(_apiVersionType);
            _httpHelper = new HttpHelper(_apiVersion);

            IsUserAuthenticated = data.IsAuthenticated;
            InvalidateProcessors();
        }
        /// <summary>
        ///     Set state data from provided json string
        /// </summary>
        public void LoadStateDataFromString(string json)
        {
            var data = SerializationHelper.DeserializeFromString<StateData>(json);
            if (!IsCustomDeviceSet)
                _deviceInfo = data.DeviceInfo;
            _user = data.UserSession;
            
            //Load Stream Edit 
            _httpRequestProcessor.RequestMessage.Username = data.UserSession.UserName;
            _httpRequestProcessor.RequestMessage.Password = data.UserSession.Password;
            
            _httpRequestProcessor.RequestMessage.DeviceId = data.DeviceInfo.DeviceId;
            _httpRequestProcessor.RequestMessage.PhoneId = data.DeviceInfo.PhoneGuid.ToString();
            _httpRequestProcessor.RequestMessage.Guid = data.DeviceInfo.DeviceGuid;
            _httpRequestProcessor.RequestMessage.AdId = data.DeviceInfo.AdId.ToString();

            foreach (var cookie in data.RawCookies)
            {
                _httpRequestProcessor.HttpHandler.CookieContainer.Add(new Uri(InstaApiConstants.INSTAGRAM_URL), cookie);
            }

            if (data.InstaApiVersion == null)
                data.InstaApiVersion = InstaApiVersionType.Version44;
            _apiVersionType = data.InstaApiVersion.Value;
            _apiVersion = InstaApiVersionList.GetApiVersionList().GetApiVersion(_apiVersionType);
            _httpHelper = new HttpHelper(_apiVersion);

            IsUserAuthenticated = data.IsAuthenticated;
            InvalidateProcessors();
        }
        /// <summary>
        ///     Set state data from provided stream asynchronously
        /// </summary>
        public async Task LoadStateDataFromStreamAsync(Stream stream)
        {
            await Task.Factory.StartNew(() =>
            {
                LoadStateDataFromStream(stream);
                Task.Delay(1000);
            });
        }
        /// <summary>
        ///     Set state data from provided json string asynchronously
        /// </summary>
        public async Task LoadStateDataFromStringAsync(string json)
        {
            await Task.Factory.StartNew(() =>
            {
                LoadStateDataFromString(json);
                Task.Delay(1000);
            });
        }

        #endregion State data

        #region private part

        private void InvalidateProcessors()
        {
            _hashtagProcessor = new HashtagProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _locationProcessor = new LocationProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _collectionProcessor = new CollectionProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _mediaProcessor = new MediaProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _userProcessor = new UserProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _storyProcessor = new StoryProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _commentProcessor = new CommentProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _messagingProcessor = new MessagingProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _feedProcessor = new FeedProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);

            _liveProcessor = new LiveProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _discoverProcessor = new DiscoverProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _accountProcessor = new AccountProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _helperProcessor = new HelperProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _tvProcessor = new TVProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);
            _businessProcessor = new BusinessProcessor(_deviceInfo, _user, _httpRequestProcessor, _logger, _userAuthValidate, this, _httpHelper);


        }

        private void ValidateUserAsync(InstaUserShortResponse user, string csrfToken, bool validateExtra = true, string password = null)
        {
            try
            {
                var converter = ConvertersFabric.Instance.GetUserShortConverter(user);
                _user.LoggedInUser = converter.Convert();
                if (password != null)
                    _user.Password = password;
                _user.UserName = _user.UserName;
                if (validateExtra)
                {
                    _user.RankToken = $"{_user.LoggedInUser.Pk}_{_httpRequestProcessor.RequestMessage.PhoneId}";
                    _user.CsrfToken = csrfToken;
                    if (string.IsNullOrEmpty(_user.CsrfToken))
                    {
                        var cookies =
                          _httpRequestProcessor.HttpHandler.CookieContainer.GetCookies(_httpRequestProcessor.Client
                              .BaseAddress);
                        _user.CsrfToken = cookies[InstaApiConstants.CSRFTOKEN]?.Value ?? string.Empty;
                    }
                    IsUserAuthenticated = true;
                    InvalidateProcessors();
                }

            }
            catch { }
        }

        private void ValidateUser()
        {
            if (string.IsNullOrEmpty(_user.UserName) || string.IsNullOrEmpty(_user.Password))
                throw new ArgumentException("user name and password must be specified");
        }

        private void ValidateLoggedIn()
        {
            if (!IsUserAuthenticated)
                throw new ArgumentException("user must be authenticated");
        }

        private void ValidateRequestMessage()
        {
            if (_httpRequestProcessor.RequestMessage == null || _httpRequestProcessor.RequestMessage.IsEmpty())
                throw new ArgumentException("API request message null or empty");
        }

        private void LogException(Exception exception)
        {
            _logger?.LogException(exception);
        }

        #endregion
    }
}