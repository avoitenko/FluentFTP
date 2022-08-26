﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Net;
using FluentFTP.Proxy;
using FluentFTP.Servers;
using FluentFTP.Rules;
using FluentFTP.Helpers;
using SysSslProtocols = System.Security.Authentication.SslProtocols;
#if !NETSTANDARD
using System.Web;
#endif
#if NETSTANDARD
#endif
#if ASYNC
using System.Threading.Tasks;

#endif
using System.Threading;

namespace FluentFTP.Client.BaseClient {
	public partial class BaseFtpClient : IDisposable {

		/// <summary>
		/// Forcibly set the capabilities of your FTP server.
		/// By default capabilities are loaded automatically after calling Connect and you don't need to use this method.
		/// This is only for advanced use-cases.
		/// </summary>
		public void SetFeatures(List<FtpCapability> capabilities) {
			m_capabilities = capabilities;
		}

		/// <summary>
		/// Performs a bitwise and to check if the specified
		/// flag is set on the <see cref="Capabilities"/>  property.
		/// </summary>
		/// <param name="cap">The <see cref="FtpCapability"/> to check for</param>
		/// <returns>True if the feature was found, false otherwise</returns>
		public bool HasFeature(FtpCapability cap) {
			if (cap == FtpCapability.NONE && Capabilities.Count == 0) {
				return true;
			}

			return Capabilities.Contains(cap);
		}

		/// <summary>
		/// Ensure a relative path is absolute by prepending the working dir
		/// </summary>
		protected string GetAbsolutePath(string path) {

			if (ServerHandler != null && ServerHandler.IsCustomGetAbsolutePath()) {
				return ServerHandler.GetAbsolutePath(this, path);
			}

			if (path == null || path.Trim().Length == 0) {
				// if path not given, then use working dir
				var pwd = GetWorkingDirectory();
				if (pwd != null && pwd.Trim().Length > 0) {
					path = pwd;
				}
				else {
					path = "/";
				}
			}

			// FIX : #153 ensure this check works with unix & windows
			// FIX : #454 OpenVMS paths can be a single character
			else if (!path.StartsWith("/") && !(path.Length > 1 && path[1] == ':')) {

				// if its a server-specific absolute path then don't add base dir
				if (ServerHandler != null && ServerHandler.IsAbsolutePath(path)) {
					return path;
				}

				// if relative path given then add working dir to calc full path
				var pwd = GetWorkingDirectory();
				if (pwd != null && pwd.Trim().Length > 0 && path != pwd) {
					if (path.StartsWith("./")) {
						path = path.Remove(0, 2);
					}

					path = (pwd + "/" + path).GetFtpPath();
				}
			}

			return path;
		}

#if ASYNC
		/// <summary>
		/// Ensure a relative path is absolute by prepending the working dir
		/// </summary>
		protected async Task<string> GetAbsolutePathAsync(string path, CancellationToken token) {

			if (ServerHandler != null && ServerHandler.IsCustomGetAbsolutePath()) {
				return await ServerHandler.GetAbsolutePathAsync(this, path, token);
			}

			if (path == null || path.Trim().Length == 0) {
				// if path not given, then use working dir
				string pwd = await GetWorkingDirectoryAsync(token);
				if (pwd != null && pwd.Trim().Length > 0) {
					path = pwd;
				}
				else {
					path = "/";
				}
			}

			// FIX : #153 ensure this check works with unix & windows
			// FIX : #454 OpenVMS paths can be a single character
			else if (!path.StartsWith("/") && !(path.Length > 1 && path[1] == ':')) {

				// if its a server-specific absolute path then don't add base dir
				if (ServerHandler != null && ServerHandler.IsAbsolutePath(path)) {
					return path;
				}

				// if relative path given then add working dir to calc full path
				string pwd = await GetWorkingDirectoryAsync(token);
				if (pwd != null && pwd.Trim().Length > 0 && path != pwd) {
					if (path.StartsWith("./")) {
						path = path.Remove(0, 2);
					}

					path = (pwd + "/" + path).GetFtpPath();
				}
			}

			return path;
		}
#endif

		/// <summary>
		/// Ensure a relative dir is absolute by prepending the working dir
		/// </summary>
		protected string GetAbsoluteDir(string path) {
			string dirPath = null;
			if (ServerHandler != null && ServerHandler.IsCustomGetAbsoluteDir()) {
				dirPath = ServerHandler.GetAbsoluteDir(this, path);
			}

			if (dirPath != null) {
				return dirPath;
			}

			path = GetAbsolutePath(path);

			path = !path.EndsWith("/") ? path + "/" : path;

			return path;
		}

#if ASYNC
		/// <summary>
		/// Ensure a relative dir is absolute by prepending the working dir
		/// </summary>
		protected async Task<string> GetAbsoluteDirAsync(string path, CancellationToken token) {
			string dirPath = null;
			if (ServerHandler != null && ServerHandler.IsCustomGetAbsoluteDir()) {
				dirPath = await ServerHandler.GetAbsoluteDirAsync(this, path, token);
			}

			if (dirPath != null) {
				return dirPath;
			}

			path = await GetAbsolutePathAsync(path, token);

			path = !path.EndsWith("/") ? path + "/" : path;

			return path;
		}
#endif

		/// <summary>
		/// Concat a path and a filename
		/// </summary>
		protected string GetAbsoluteFilePath(string path, string fileName)
		{
			string filePath = null;
			if (ServerHandler != null && ServerHandler.IsCustomGetAbsoluteFilePath()) {
				filePath = ServerHandler.GetAbsoluteFilePath(this, path, fileName);
			}

			if (filePath != null) {
				return filePath;
			}

			path = !path.EndsWith("/") ? path + "/" + fileName : path + fileName;

			return path;
		}

#if ASYNC
		/// <summary>
		/// Concat a path and a filename
		/// </summary>
		protected async Task<string> GetAbsoluteFilePathAsync(string path, string fileName, CancellationToken token) {
			string filePath = null;
			if (ServerHandler != null && ServerHandler.IsCustomGetAbsoluteFilePath()) {
				filePath = await ServerHandler.GetAbsoluteFilePathAsync(this, path, fileName, token);
			}

			if (filePath != null) {
				return filePath;
			}

			path = !path.EndsWith("/") ? path + "/" + fileName : path + fileName;

			return path;
		}
#endif


		protected static string DecodeUrl(string url) {
			return WebUtility.UrlDecode(url);
		}

		/// <summary>
		/// Disables UTF8 support and changes the Encoding property
		/// back to ASCII. If the server returns an error when trying
		/// to turn UTF8 off a FtpCommandException will be thrown.
		/// </summary>
		public void DisableUTF8() {
			FtpReply reply;

			lock (m_lock) {
				if (!(reply = Execute("OPTS UTF8 OFF")).Success) {
					throw new FtpCommandException(reply);
				}

				m_textEncoding = Encoding.ASCII;
				m_textEncodingAutoUTF = false;
			}

		}

		/// <summary>
		/// Data shouldn't be on the socket, if it is it probably means we've been disconnected.
		/// Read and discard whatever is there and optionally close the connection.
		/// Returns the stale data as text, if any, or null if none was found.
		/// </summary>
		/// <param name="closeStream">close the connection?</param>
		/// <param name="evenEncrypted">even read encrypted data?</param>
		/// <param name="traceData">trace data to logs?</param>
		protected string ReadStaleData(bool closeStream, bool evenEncrypted, bool traceData) {
			string staleData = null;
			if (m_stream != null && m_stream.SocketDataAvailable > 0) {
				if (traceData) {
					LogStatus(FtpTraceLevel.Info, "There is stale data on the socket, maybe our connection timed out or you did not call GetReply(). Re-connecting...");
				}

				if (m_stream.IsConnected && (!m_stream.IsEncrypted || evenEncrypted)) {
					var buf = new byte[m_stream.SocketDataAvailable];
					m_stream.RawSocketRead(buf);
					staleData = Encoding.GetString(buf).TrimEnd('\r', '\n');
					if (traceData) {
						LogStatus(FtpTraceLevel.Verbose, "The stale data was: " + staleData);
					}
					if(string.IsNullOrEmpty(staleData)) {
						closeStream = false;
					}
				}

				if (closeStream) {
					m_stream.Close();
				}
			}
			return staleData;
		}

#if ASYNC
		/// <summary>
		/// Data shouldn't be on the socket, if it is it probably means we've been disconnected.
		/// Read and discard whatever is there and optionally close the connection.
		/// Returns the stale data as text, if any, or null if none was found.
		/// </summary>
		/// <param name="closeStream">close the connection?</param>
		/// <param name="evenEncrypted">even read encrypted data?</param>
		/// <param name="traceData">trace data to logs?</param>
		/// <param name="token">The token that can be used to cancel the entire process</param>
		protected async Task<string> ReadStaleDataAsync(bool closeStream, bool evenEncrypted, bool traceData, CancellationToken token) {
			string staleData = null;
			if (m_stream != null && m_stream.SocketDataAvailable > 0) {
				if (traceData) {
					LogStatus(FtpTraceLevel.Info, "There is stale data on the socket, maybe our connection timed out or you did not call GetReply(). Re-connecting...");
				}

				if (m_stream.IsConnected && (!m_stream.IsEncrypted || evenEncrypted)) {
					var buf = new byte[m_stream.SocketDataAvailable];
					await m_stream.RawSocketReadAsync(buf, token);
					staleData = Encoding.GetString(buf).TrimEnd('\r', '\n');
					if (traceData) {
						LogStatus(FtpTraceLevel.Verbose, "The stale data was: " + staleData);
					}
				}

				if (closeStream) {
					m_stream.Close();
				}
			}
			return staleData;
		}
#endif

		/// <summary>
		/// Checks if this FTP/FTPS connection is made through a proxy.
		/// </summary>
		public bool IsProxy() {
			return this is FtpClientProxy;
		}
		
		/// <summary>
		/// Returns true if the file passes all the rules
		/// </summary>
		protected bool FilePassesRules(FtpResult result, List<FtpRule> rules, bool useLocalPath, FtpListItem item = null) {
			if (rules != null && rules.Count > 0) {
				var passes = FtpRule.IsAllAllowed(rules, item ?? result.ToListItem(useLocalPath));
				if (!passes) {

					LogStatus(FtpTraceLevel.Info, "Skipped file due to rule: " + (useLocalPath ? result.LocalPath : result.RemotePath));

					// mark that the file was skipped due to a rule
					result.IsSkipped = true;
					result.IsSkippedByRule = true;

					// skip uploading the file
					return false;
				}
			}
			return true;
		}

	}
}