using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Proyecto26.Common;

namespace Proyecto26
{
    public static class HttpBase
    {
        private const int HTTP_NO_CONTENT = 204;

        public static IEnumerator CreateRequestAndRetry(RequestHelper options, Action<RequestException, ResponseHelper> callback)
        {
            var retries = 0;
            do
            {
                using (var request = CreateRequest(options))
                {
                    // send + drive the request non‑blockingly, frame by frame
                    yield return SendUnityWebRequestCoroutine(request, options);

                    // now build the response as before
                    var response = request.CreateWebResponse();

                    if (request.IsValidRequest(options))
                    {
                        DebugLog(options.EnableDebug,
                                 $"RestClient - Response\nUrl: {options.Uri}\nMethod: {options.Method}\nStatus: {request.responseCode}\nResponse: {(options.ParseResponseBody ? response.Text : "body not parsed")}",
                                 false);
                        callback(null, response);
                        yield break;
                    }
                    else if (!options.IsAborted
                             && retries < options.Retries
                             && (!options.RetryCallbackOnlyOnNetworkErrors || IsNetworkError(request)))
                    {
                        options.RetryCallback?.Invoke(CreateException(options, request), retries);
                        retries++;
                        DebugLog(options.EnableDebug,
                                 $"RestClient - Retry Request (attempt {retries})\nUrl: {options.Uri}\nMethod: {options.Method}",
                                 false);
                        yield return new WaitForSeconds(options.RetrySecondsDelay);
                    }
                    else
                    {
                        var err = CreateException(options, request);
                        DebugLog(options.EnableDebug, err, true);
                        callback(err, response);
                        yield break;
                    }
                }
            }
            while (retries <= options.Retries);
        }

        private static IEnumerator SendUnityWebRequestCoroutine(UnityWebRequest request, RequestHelper options)
        {
            var op = request.SendWebRequest();               // <<< core send
            options.ProgressCallback?.Invoke(0f);

            while (!op.isDone)
            {
                options.ProgressCallback?.Invoke(op.progress);
                yield return null;                            // <<< hand control back to Unity each frame
            }

            options.ProgressCallback?.Invoke(1f);
        }

        private static UnityWebRequest CreateRequest(RequestHelper options)
        {
            var url = options.Uri.BuildUrl(options.Params);
            DebugLog(options.EnableDebug, string.Format("RestClient - Request\nUrl: {0}", url), false);
            if (options.FormData is WWWForm && options.Method == UnityWebRequest.kHttpVerbPOST)
            {
                return UnityWebRequest.Post(url, options.FormData);
            }
            else
            {
                return new UnityWebRequest(url, options.Method);
            }
        }

        private static RequestException CreateException(RequestHelper options, UnityWebRequest request)
        {
            bool IsNetworkError;
            bool IsHttpError;
#if UNITY_2020_2_OR_NEWER
            IsNetworkError = (request.result == UnityWebRequest.Result.ConnectionError);
            IsHttpError = (request.result == UnityWebRequest.Result.ProtocolError);
#else
            IsNetworkError = request.isNetworkError;
            IsHttpError = request.isHttpError;
#endif
            return new RequestException(options, request.error, IsHttpError, IsNetworkError, request.responseCode, options.ParseResponseBody ? request.downloadHandler.text : "body not parsed");
        }

        private static bool IsNetworkError(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.ConnectionError;
#else
            return request.isNetworkError;
#endif
        }

        public static void DebugLog(bool debugEnabled, object message, bool isError)
        {
            if (debugEnabled)
            {
                if (isError)
                    Debug.LogError(message);
                else
                    Debug.Log(message);
            }
        }

        public static IEnumerator DefaultUnityWebRequest(RequestHelper options, Action<RequestException, ResponseHelper> callback)
        {
            return CreateRequestAndRetry(options, callback);
        }

        public static IEnumerator DefaultUnityWebRequest<TResponse>(RequestHelper options, Action<RequestException, ResponseHelper, TResponse> callback)
        {
            return CreateRequestAndRetry(options, (RequestException err, ResponseHelper res) =>
            {
                var body = default(TResponse);
                try
                {
                    if (err == null && res.StatusCode != HTTP_NO_CONTENT && res.Data != null && options.ParseResponseBody)
                        body = JsonUtility.FromJson<TResponse>(res.Text);
                }
                catch (Exception error)
                {
                    DebugLog(options.EnableDebug, string.Format("RestClient - Invalid JSON format\nError: {0}", error.Message), true);
                    err = new RequestException(error.Message);
                }
                finally
                {
                    callback(err, res, body);
                }
            });
        }

        public static IEnumerator DefaultUnityWebRequest<TResponse>(RequestHelper options, Action<RequestException, ResponseHelper, TResponse[]> callback)
        {
            return CreateRequestAndRetry(options, (RequestException err, ResponseHelper res) =>
            {
                var body = default(TResponse[]);
                try
                {
                    if (err == null && res.StatusCode != HTTP_NO_CONTENT && res.Data != null && options.ParseResponseBody)
                        body = JsonHelper.ArrayFromJson<TResponse>(res.Text);
                }
                catch (Exception error)
                {
                    DebugLog(options.EnableDebug, string.Format("RestClient - Invalid JSON format\nError: {0}", error.Message), true);
                    err = new RequestException(error.Message);
                }
                finally
                {
                    callback(err, res, body);
                }
            });
        }
    }
}
