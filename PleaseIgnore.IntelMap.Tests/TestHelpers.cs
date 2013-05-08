using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace PleaseIgnore.IntelMap.Tests {
    internal static class TestHelpers {
        public const string TestScheme = "test";
        private static IWebRequestCreate requestProxy;
        private static Mock<IWebRequestCreate> requestMock;

        /// <summary>
        ///     We use a session-test://whatever URI to connect to our mock
        /// </summary>
        private class WebRequestCreateProxy : IWebRequestCreate {
            public WebRequest Create(Uri uri) {
                return requestProxy.Create(uri);
            }
        }

        /// <summary>
        ///     Cleans up static state after a unit test.
        /// </summary>
        public static void Cleanup() {
            requestProxy = null;
            requestMock = null;
        }

        public static void RegisterRequestHandler(IWebRequestCreate proxy) {
            WebRequest.RegisterPrefix(TestScheme, new WebRequestCreateProxy());
            requestProxy = proxy;
        }

        public static void RegisterRequestHandler(Uri requestUri, WebRequest request) {
            if (requestMock == null) {
                requestMock = new Mock<IWebRequestCreate>(MockBehavior.Strict);
                requestProxy = requestMock.Object;
                WebRequest.RegisterPrefix(TestScheme, new WebRequestCreateProxy());
            }
            requestMock.Setup(x => x.Create(requestUri)).Returns(request);
        }

        /// <summary>
        ///     Sets up a proxy of <see cref="IWebRequestCreate"/> that will
        ///     respond with a specific string as its payload.
        /// </summary>
        /// <returns>
        ///     Instance of <see cref="StringBuilder"/> that will receive the
        ///     request payload.
        /// </returns>
        public static StringBuilder CreateRequestMock(Uri requestUri, string responseText) {
            var responseBody = Encoding.UTF8.GetBytes(responseText);
            var responseStream = new MemoryStream(responseBody, false);
            var responseMock = new Mock<WebResponse>();
            responseMock.Setup(x => x.GetResponseStream())
                .Returns(responseStream);
            var response = responseMock.Object;

            var requestBody = new StringBuilder();
            var requestStreamMock = new Mock<Stream>();
            requestStreamMock.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] bytes, int index, int count)
                    => requestBody.Append(Encoding.UTF8.GetString(bytes, index, count)));
            var requestStream = requestStreamMock.Object;

            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(requestStream);
            requestMock.Setup(x => x.GetResponse())
                .Returns(response);
            var request = requestMock.Object;

            RegisterRequestHandler(requestUri, requestMock.Object);
            return requestBody;
        }

        /// <summary>
        ///     Sets up a proxy of <see cref="IWebRequestCreate"/> that will
        ///     throw <see cref="WebException"/> when generating the response.
        /// </summary>
        /// <returns>
        ///     Instance of <see cref="StringBuilder"/> that will receive the
        ///     request payload.
        /// </returns>
        public static StringBuilder CreateRequestError<TException>(Uri requestUri)
                where TException : Exception, new() {
            var requestBody = new StringBuilder();
            var requestStreamMock = new Mock<Stream>();
            requestStreamMock.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] bytes, int index, int count)
                    => requestBody.Append(Encoding.UTF8.GetString(bytes, index, count)));
            var requestStream = requestStreamMock.Object;

            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(requestStream);
            requestMock.Setup(x => x.GetResponse())
                .Throws<TException>();
            var request = requestMock.Object;

            RegisterRequestHandler(requestUri, requestMock.Object);
            return requestBody;
        }
    }
}
