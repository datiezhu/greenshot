﻿#region Greenshot GNU General Public License

// Greenshot - a free and open source screenshot tool
// Copyright (C) 2007-2018 Thomas Braun, Jens Klingen, Robin Krom
// 
// For more information see: http://getgreenshot.org/
// The Greenshot project is hosted on GitHub https://github.com/greenshot/greenshot
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 1 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

#endregion

#region Usings

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Xml;
using Dapplo.Ini;
using Dapplo.Log;
using Greenshot.Addons.Core;
using Greenshot.Addons.Interfaces;
using Greenshot.Addons.Interfaces.Plugin;

#endregion

namespace Greenshot.Addon.Flickr
{
	/// <summary>
	///     Description of FlickrUtils.
	/// </summary>
	public class FlickrUtils
	{
		private const string FLICKR_API_BASE_URL = "https://api.flickr.com/services/";
		private const string FLICKR_UPLOAD_URL = FLICKR_API_BASE_URL + "upload/";
		// OAUTH
		private const string FLICKR_OAUTH_BASE_URL = FLICKR_API_BASE_URL + "oauth/";
		private const string FLICKR_ACCESS_TOKEN_URL = FLICKR_OAUTH_BASE_URL + "access_token";
		private const string FLICKR_AUTHORIZE_URL = FLICKR_OAUTH_BASE_URL + "authorize";
		private const string FLICKR_REQUEST_TOKEN_URL = FLICKR_OAUTH_BASE_URL + "request_token";
		private const string FLICKR_FARM_URL = "https://farm{0}.staticflickr.com/{1}/{2}_{3}_o.{4}";
		// REST
		private const string FLICKR_REST_URL = FLICKR_API_BASE_URL + "rest/";
		private const string FLICKR_GET_INFO_URL = FLICKR_REST_URL + "?method=flickr.photos.getInfo";
		private static readonly LogSource Log = new LogSource();
		private static readonly IFlickrConfiguration config = IniConfig.Current.Get<IFlickrConfiguration>();

		/// <summary>
		///     Do the actual upload to Flickr
		///     For more details on the available parameters, see: http://flickrnet.codeplex.com
		/// </summary>
		/// <param name="surfaceToUpload"></param>
		/// <param name="outputSettings"></param>
		/// <param name="title"></param>
		/// <param name="filename"></param>
		/// <returns>url to image</returns>
		public static string UploadToFlickr(ISurface surfaceToUpload, SurfaceOutputSettings outputSettings, string title, string filename)
		{
			var oAuth = new OAuthSession(FlickrCredentials.ConsumerKey, FlickrCredentials.ConsumerSecret)
			{
				BrowserSize = new Size(520, 800),
				CheckVerifier = false,
				AccessTokenUrl = FLICKR_ACCESS_TOKEN_URL,
				AuthorizeUrl = FLICKR_AUTHORIZE_URL,
				RequestTokenUrl = FLICKR_REQUEST_TOKEN_URL,
				LoginTitle = "Flickr authorization",
				Token = config.FlickrToken,
				TokenSecret = config.FlickrTokenSecret
			};
			if (string.IsNullOrEmpty(oAuth.Token))
			{
				if (!oAuth.Authorize())
				{
					return null;
				}
				if (!string.IsNullOrEmpty(oAuth.Token))
				{
					config.FlickrToken = oAuth.Token;
				}
				if (!string.IsNullOrEmpty(oAuth.TokenSecret))
				{
					config.FlickrTokenSecret = oAuth.TokenSecret;
				}
			}
			try
			{
                IDictionary<string, object> signedParameters = new Dictionary<string, object>
                {
                    { "content_type", "2" }, // Screenshot
                    { "tags", "Greenshot" },
                    { "is_public", config.IsPublic ? "1" : "0" },
                    { "is_friend", config.IsFriend ? "1" : "0" },
                    { "is_family", config.IsFamily ? "1" : "0" },
                    { "safety_level", $"{(int)config.SafetyLevel}" },
                    { "hidden", config.HiddenFromSearch ? "1" : "2" }
                };
                IDictionary<string, object> otherParameters = new Dictionary<string, object>
                {
                    { "photo", new SurfaceContainer(surfaceToUpload, outputSettings, filename) }
                };
                var response = oAuth.MakeOAuthRequest(HTTPMethod.POST, FLICKR_UPLOAD_URL, signedParameters, otherParameters, null);
				var photoId = GetPhotoId(response);

				// Get Photo Info
				signedParameters = new Dictionary<string, object> {{"photo_id", photoId}};
				var photoInfo = oAuth.MakeOAuthRequest(HTTPMethod.POST, FLICKR_GET_INFO_URL, signedParameters, null, null);
				return GetUrl(photoInfo);
			}
			catch (Exception ex)
			{
				Log.Error().WriteLine(ex, "Upload error: ");
				throw;
			}
			finally
			{
				if (!string.IsNullOrEmpty(oAuth.Token))
				{
					config.FlickrToken = oAuth.Token;
				}
				if (!string.IsNullOrEmpty(oAuth.TokenSecret))
				{
					config.FlickrTokenSecret = oAuth.TokenSecret;
				}
			}
		}

		private static string GetUrl(string response)
		{
			try
			{
				var doc = new XmlDocument();
				doc.LoadXml(response);
				if (config.UsePageLink)
				{
					var nodes = doc.GetElementsByTagName("url");
					if (nodes.Count > 0)
					{
						var xmlNode = nodes.Item(0);
						if (xmlNode != null)
						{
							return xmlNode.InnerText;
						}
					}
				}
				else
				{
					var nodes = doc.GetElementsByTagName("photo");
					if (nodes.Count > 0)
					{
						var item = nodes.Item(0);
						if (item?.Attributes != null)
						{
							var farmId = item.Attributes["farm"].Value;
							var serverId = item.Attributes["server"].Value;
							var photoId = item.Attributes["id"].Value;
							var originalsecret = item.Attributes["originalsecret"].Value;
							var originalFormat = item.Attributes["originalformat"].Value;
							return string.Format(FLICKR_FARM_URL, farmId, serverId, photoId, originalsecret, originalFormat);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error().WriteLine(ex, "Error parsing Flickr Response.");
			}
			return null;
		}

		private static string GetPhotoId(string response)
		{
			try
			{
				var doc = new XmlDocument();
				doc.LoadXml(response);
				var nodes = doc.GetElementsByTagName("photoid");
				if (nodes.Count > 0)
				{
					var xmlNode = nodes.Item(0);
					if (xmlNode != null)
					{
						return xmlNode.InnerText;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error().WriteLine(ex, "Error parsing Flickr Response.");
			}
			return null;
		}
	}
}