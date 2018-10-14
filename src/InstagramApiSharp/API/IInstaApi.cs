﻿/*  
 *  
 *  
 *  Base of everything! Access to any other classes via IInstaApi
 *  
 *  
 *                      IRANIAN DEVELOPERS
 *        
 *        
 *                            2018
 *  
 *  
 */

using System.IO;
using System.Threading.Tasks;
using InstagramApiSharp.API.Processors;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.Android.DeviceInfo;
using InstagramApiSharp.Enums;

namespace InstagramApiSharp.API
{
    /// <summary>
    ///     Base of everything that you want.
    /// </summary>
    public interface IInstaApi
    {
        #region Properties

        /// <summary>
        ///     Indicates whether user authenticated or not
        /// </summary>
        bool IsUserAuthenticated { get; }

        /// <summary>
        ///     Live api functions.
        /// </summary>
        ILiveProcessor LiveProcessor { get; }
        /// <summary>
        ///     Discover api functions.
        /// </summary>
        IDiscoverProcessor DiscoverProcessor { get; }
        /// <summary>
        ///     Account api functions.
        /// </summary>
        IAccountProcessor AccountProcessor { get; }
        /// <summary>
        ///     Story api functions.
        /// </summary>
        IStoryProcessor StoryProcessor { get; }
        /// <summary>
        ///     Media api functions.
        /// </summary>
        IMediaProcessor MediaProcessor { get; }
        /// <summary>
        ///     Comments api functions.
        /// </summary>
        ICommentProcessor CommentProcessor { get; }
        /// <summary>
        ///     Messaging (direct) api functions.
        /// </summary>
        IMessagingProcessor MessagingProcessor { get; }
        /// <summary>
        ///     Feed api functions.
        /// </summary>
        IFeedProcessor FeedProcessor { get; }
        /// <summary>
        ///     Collection api functions.
        /// </summary>
        ICollectionProcessor CollectionProcessor { get; }
        /// <summary>
        ///     Location api functions.
        /// </summary>
        ILocationProcessor LocationProcessor { get; }
        /// <summary>
        ///     Hashtag api functions.
        /// </summary>
        IHashtagProcessor HashtagProcessor { get; }
        /// <summary>
        ///     User api functions.
        /// </summary>
        IUserProcessor UserProcessor { get; }
        /// <summary>
        ///     Instagram TV api functions.
        /// </summary>
        ITVProcessor TVProcessor { get; }
        /// <summary>
        ///     Business api functions
        ///     <para>Note: All functions of this interface only works with business accounts!</para>
        /// </summary>
        IBusinessProcessor BusinessProcessor { get; }
        #endregion

        #region State data

        /// <summary>
        ///     Get current state info as Memory stream
        /// </summary>
        /// <returns>State data</returns>
        Stream GetStateDataAsStream();
        /// <summary>
        ///     Get current state info as Json string
        /// </summary>
        /// <returns>State data</returns>
        string GetStateDataAsString();
        /// <summary>
        ///     Get current state info as Json string asynchronously
        /// </summary>
        /// <returns>
        ///     State data
        /// </returns>
        Task<string> GetStateDataAsStringAsync();
        /// <summary>
        ///     Get current state info as Memory stream asynchronously
        /// </summary>
        /// <returns>State data</returns>
        Task<Stream> GetStateDataAsStreamAsync();
        /// <summary>
        ///     Set state data from provided stream
        /// </summary>
        void LoadStateDataFromStream(Stream data);
        /// <summary>
        ///     Set state data from provided json string
        /// </summary>
        void LoadStateDataFromString(string data);
        /// <summary>
        ///     Set state data from provided stream asynchronously
        /// </summary>
        Task LoadStateDataFromStreamAsync(Stream stream);
        /// <summary>
        ///     Set state data from provided json string asynchronously
        /// </summary>
        Task LoadStateDataFromStringAsync(string json);

        #endregion State data

        #region Other public functions
        /// <summary>
        ///     Gets current device
        /// </summary>
        AndroidDevice GetCurrentDevice();
        /// <summary>
        ///     Gets logged in user
        /// </summary>
        UserSessionData GetLoggedUser();
        /// <summary>
        ///     Get Accept Language
        /// </summary>
        string GetAcceptLanguage();
        /// <summary>
        ///     Set delay between requests. Useful when API supposed to be used for mass-bombing.
        /// </summary>
        /// <param name="delay">Timespan delay</param>
        void SetRequestDelay(IRequestDelay delay);
        /// <summary>
        ///     Set instagram api version (for user agent version)
        /// </summary>
        /// <param name="apiVersion">Api version</param>
        void SetApiVersion(InstaApiVersionType apiVersion);
        /// <summary>
        ///     Set custom android device.
        ///     <para>Note 1: If you want to use this method, you should call it before you calling <seealso cref="IInstaApi.LoadStateDataFromStream(Stream)"/> or <seealso cref="IInstaApi.LoadStateDataFromString(string)"/></para>
        ///     <para>Note 2: this is optional, if you didn't set this, <seealso cref="InstagramApiSharp"/> will choose random device.</para>
        /// </summary>
        /// <param name="device">Android device</param>
        void SetDevice(AndroidDevice device);
        /// <summary>
        ///     Set Accept Language
        /// </summary>
        /// <param name="languageCodeAndCountryCode">Language Code and Country Code. For example:
        /// <para>en-US for united states</para>
        /// <para>fa-IR for IRAN</para>
        /// </param>
        bool SetAcceptLanguage(string languageCodeAndCountryCode);
        
        #endregion Other public functions

        #region Authentication, challenge functions

        #region Challenge part
        /// <summary>
        ///     Get challenge require (checkpoint required) options
        /// </summary>
        Task<IResult<InstaChallengeRequireVerifyMethod>> GetChallengeRequireVerifyMethodAsync();
        /// <summary>
        ///     Reset challenge require (checkpoint required) method
        /// </summary>
        Task<IResult<InstaChallengeRequireVerifyMethod>> ResetChallengeRequireVerifyMethodAsync();
        /// <summary>
        ///     Request verification code sms for challenge require (checkpoint required)
        /// </summary>
        Task<IResult<InstaChallengeRequireSMSVerify>> RequestVerifyCodeToSMSForChallengeRequireAsync();
        /// <summary>
        ///     Submit phone number for challenge require (checkpoint required)
        ///     <para>Note: This only needs , when you calling <see cref="IInstaApi.GetChallengeRequireVerifyMethodAsync"/> or
        ///     <see cref="IInstaApi.ResetChallengeRequireVerifyMethodAsync"/> and
        ///     <see cref="InstaChallengeRequireVerifyMethod.SubmitPhoneRequired"/> property is true.</para>
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        Task<IResult<InstaChallengeRequireSMSVerify>> SubmitPhoneNumberForChallengeRequireAsync(string phoneNumber);
        /// <summary>
        ///     Request verification code email for challenge require (checkpoint required)
        /// </summary>
        Task<IResult<InstaChallengeRequireEmailVerify>> RequestVerifyCodeToEmailForChallengeRequireAsync();
        /// <summary>
        ///     Verify verification code for challenge require (checkpoint required)
        /// </summary>
        /// <param name="verifyCode">Verification code</param>
        Task<IResult<InstaLoginResult>> VerifyCodeForChallengeRequireAsync(string verifyCode);
        #endregion Challenge part

        /// <summary>
        ///     Set cookie and html document to verify login information.
        /// </summary>
        /// <param name="htmlDocument">Html document source</param>
        /// <param name="cookies">Cookies from webview or webbrowser control</param>
        /// <returns>True if logged in, False if not</returns>
        Task<IResult<bool>> SetCookiesAndHtmlForFacebookLoginAsync(string htmlDocument, string cookies,bool validate = true);
        /// <summary>
        ///     Set cookie and web browser response object to verify login information.
        /// </summary>
        /// <param name="webBrowserResponse">Web browser response object</param>
        /// <param name="cookies">Cookies from webview or webbrowser control</param>
        /// <returns>True if logged in, False if not</returns>
        Task<IResult<bool>> SetCookiesAndHtmlForFacebookLogin(InstaWebBrowserResponse webBrowserResponse, string cookies, bool validate = true);

        /*Task<IResult<bool>> SetCookiesAndHtmlForFacebookLogin(InstaFacebookAccountInfo facebookAccount, string cookies);*/
        /// <summary>
        ///     Check email availability
        /// </summary>
        /// <param name="email">Email to check</param>
        Task<IResult<InstaCheckEmailRegistration>> CheckEmailAsync(string email);
        /// <summary>
        ///     Check phone number availability
        /// </summary>
        /// <param name="phoneNumber">Phone number to check</param>
        Task<IResult<bool>> CheckPhoneNumberAsync(string phoneNumber);
        /// <summary>
        ///     Check username availablity. 
        /// </summary>
        /// <param name="username">Username</param>
        Task<IResult<InstaAccountCheck>> CheckUsernameAsync(string username);
        /// <summary>
        ///     Send sign up sms code
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        Task<IResult<bool>> SendSignUpSmsCodeAsync(string phoneNumber);
        /// <summary>
        ///     Verify sign up sms code
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        /// <param name="verificationCode">Verification code</param>
        Task<IResult<InstaPhoneNumberRegistration>> VerifySignUpSmsCodeAsync(string phoneNumber, string verificationCode);
        /// <summary>
        ///     Get username suggestions
        /// </summary>
        /// <param name="name">Name</param>
        Task<IResult<InstaRegistrationSuggestionResponse>> GetUsernameSuggestionsAsync(string name);
        /// <summary>
        ///     Validate new account creation with phone number
        /// </summary>
        /// <param name="phoneNumber">Phone number</param>
        /// <param name="verificationCode">Verification code</param>
        /// <param name="username">Username to set</param>
        /// <param name="password">Password to set</param>
        /// <param name="firstName">First name to set</param>
        Task<IResult<InstaAccountCreation>> ValidateNewAccountWithPhoneNumberAsync(string phoneNumber, string verificationCode, string username, string password, string firstName);
        /// <summary>
        ///     Create a new instagram account
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        /// <param name="email">Email</param>
        /// <param name="firstName">First name (optional)</param>
        /// <param name="delay">Delay between requests. null = 2.5 seconds</param>
        Task<IResult<InstaAccountCreation>> CreateNewAccountAsync(string username, string password, string email, string firstName = ""/*, TimeSpan? delay = null*/);        
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
        Task<IResult<InstaLoginResult>> LoginAsync(bool isNewLogin = true);
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
        Task<IResult<InstaLoginTwoFactorResult>> TwoFactorLoginAsync(string verificationCode);

        /// <summary>
        ///     Get Two Factor Authentication details
        /// </summary>
        /// <returns>
        ///     An instance of TwoFactorLoginInfo if success.
        ///     A null reference if not success; in this case, do LoginAsync first and check if Two Factor Authentication is
        ///     required, if not, don't run this method
        /// </returns>
        Task<IResult<InstaTwoFactorLoginInfo>> GetTwoFactorInfoAsync();
        /// <summary>
        ///     Send recovery code by Username
        /// </summary>
        /// <param name="username">Username</param>
        Task<IResult<InstaRecovery>> SendRecoveryByUsernameAsync(string username);
        /// <summary>
        ///     Send recovery code by Email
        /// </summary>
        /// <param name="email">Email Address</param>
        Task<IResult<InstaRecovery>> SendRecoveryByEmailAsync(string email);
        /// <summary>
        ///     Send recovery code by Phone number
        /// </summary>
        /// <param name="phone">Phone Number</param>
        Task<IResult<InstaRecovery>> SendRecoveryByPhoneAsync(string phone);
        /// <summary>
        ///    Send Two Factor Login SMS Again
        /// </summary>
        Task<IResult<TwoFactorLoginSMS>> SendTwoFactorLoginSMSAsync();
        /// <summary>
        ///     Logout from instagram asynchronously
        /// </summary>
        /// <returns>True if logged out without errors</returns>
        Task<IResult<bool>> LogoutAsync();
        /// <summary>
        ///     Get currently logged in user info asynchronously
        /// </summary>
        /// <returns>
        ///     <see cref="InstaCurrentUser" />
        /// </returns>
        Task<IResult<InstaCurrentUser>> GetCurrentUserAsync();
        
        #endregion Authentication, challenge functions
    }
}