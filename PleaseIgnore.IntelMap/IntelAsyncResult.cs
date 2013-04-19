using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Implementation of <see cref="IAsyncResult"/>, providing common
    ///     support for asynchronous function calls in PleaseIgnore.
    /// </summary>
    /// <typeparam name="TOwner">
    ///     The object type that creates this <see cref="IntelAsyncResult`2"/>.
    /// </typeparam>
    /// <typeparam name="TResult">
    ///     The return <see cref="Type"/> of the <c>End<var>Name</var></c>
    ///     call.
    /// </typeparam>
    internal class IntelAsyncResult<TOwner, TResult> : IAsyncResult
            where TOwner : class {
        // The callback to call when execution has completed
        private readonly AsyncCallback callback;
        // The value of AsyncState
        private readonly object state;
        // The object that created this IntelAsyncResult
        private readonly TOwner owner;
        // Number of waits made on End*()
        private int waitCount;
        // Set to true once the asynchronous execution has completed
        private bool completed;
        // Set to true if the execution was completed synchronously
        private bool synchronous;
        // The return value of a successful asynchronous execution
        private TResult result;
        // The exception object for an unsuccessful asychronous execution
        private Exception exception;
        // The WaitHandle instance to use when waiting for completion.  Populated
        // by AsyncWaitHandle only as needed.
        private ManualResetEvent waitHandle;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelAsyncResult`2"/>
        ///     class.
        /// </summary>
        /// <param name="owner">
        ///     The instance of <see cref="TOwner"/> that created this object.
        /// </param>
        /// <param name="callback">
        ///     The <see cref="AsyncCallback"/> to call when the asynchronous
        ///     operation completes.  This can be <see langword="null"/>.
        /// </param>
        /// <param name="state">
        ///     The value of <see cref="AsyncState"/>.  This can be
        ///     <see langword="null"/>.
        /// </param>
        protected IntelAsyncResult(TOwner owner, AsyncCallback callback, object state) {
            Contract.Requires(owner != null);
            this.owner = owner;
            this.callback = callback;
            this.state = state;
        }

        /// <summary>
        ///     Gets a user-defined object that qualifies or contains information
        ///     about an asynchronous operation.
        /// </summary>
        public object AsyncState { get { return this.state; } }

        /// <summary>
        ///     Gets a value that indicates whether the asynchronous operation
        ///     completed synchronously.
        /// </summary>
        public bool CompletedSynchronously { get { return this.synchronous; } }

        /// <summary>
        ///     Gets a value that indicates whether the asynchronous operation
        ///     has completed.
        /// </summary>
        public bool IsCompleted { get { return this.completed; } }

        /// <summary>
        ///     Gets a <see cref="WaitHandle"/> that is used to wait for an
        ///     asynchronous operation to complete.
        /// </summary>
        public WaitHandle AsyncWaitHandle {
            get {
                var handle = new ManualResetEvent(false);
                var oldhandle = Interlocked.CompareExchange(
                    ref this.waitHandle,
                    handle,
                    null);
                if (this.completed) {
                    this.waitHandle.Set();
                }
                if (oldhandle != null) {
                    handle.Close();
                }
                return this.waitHandle;
            }
        }

        /// <summary>
        ///     Gets the instance of <see cref="TOwner"/> executing this
        ///     <see cref="IAsyncResult"/>.
        /// </summary>
        protected TOwner Owner { get { return this.owner; } }

        /// <summary>
        ///     Signals that the asychronous operation has completed
        ///     successfully.
        /// </summary>
        /// <param name="result">
        ///     The return code for the <c>End*()</c> method.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be queued onto the
        ///     <see cref="ThreadPool"/>.
        /// </remarks>
        protected void AsyncComplete(TResult result) {
            Debug.Assert(!this.completed);
            this.completed = true;
            this.result = result;
            Thread.MemoryBarrier();
            if (this.waitHandle != null) {
                this.waitHandle.Set();
            }

            if (this.callback != null) {
                ThreadPool.QueueUserWorkItem(this.UserCallback);
            }
        }

        /// <summary>
        ///     Signals that the asychronous operation was aborted due to
        ///     an error.
        /// </summary>
        /// <param name="exception">
        ///     The exception to throw from the <c>End*()</c> method.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be queued onto the
        ///     <see cref="ThreadPool"/>.
        /// </remarks>
        protected void AsyncComplete(Exception exception) {
            Contract.Requires(exception != null);
            Debug.Assert(!this.completed);
            this.completed = true;
            this.exception = exception;
            Thread.MemoryBarrier();
            if (this.waitHandle != null) {
                this.waitHandle.Set();
            }

            if (callback != null) {
                ThreadPool.QueueUserWorkItem(this.UserCallback);
            }
        }

        /// <summary>
        ///     Signals that the asychronous operation has completed
        ///     successfully, but the client should be notified synchronously.
        /// </summary>
        /// <param name="result">
        ///     The return code for the <c>End*()</c> method.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be called immediately.
        /// </remarks>
        protected void SyncComplete(TResult result) {
            Debug.Assert(!this.completed);
            this.completed = true;
            this.synchronous = true;
            this.result = result;
            Thread.MemoryBarrier();
            if (this.waitHandle != null) {
                this.waitHandle.Set();
            }

            if (callback != null) {
                callback(this);
            }
        }

        /// <summary>
        ///     Signals that the asychronous operation was aborted due to an
        ///     error, but the client should be notified synchronously.
        /// </summary>
        /// <param name="exception">
        ///     The exception to throw from the <c>End*()</c> method.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be called immediately.
        /// </remarks>
        protected void SyncComplete(Exception exception) {
            Contract.Requires(exception != null);
            Debug.Assert(!this.completed);
            this.completed = true;
            this.synchronous = true;
            this.exception = exception;
            Thread.MemoryBarrier();
            if (this.waitHandle != null) {
                this.waitHandle.Set();
            }

            if (callback != null) {
                callback(this);
            }
        }

        /// <summary>
        ///     Waits for the asynchronous call to complete, returning the queued
        ///     value or throwing an exception as appropriate.
        /// </summary>
        /// <returns>
        ///     The value provided to <see cref="AsyncComplete(TResult)"/> or
        ///     <see cref="SyncComplete(TResult)"/>.
        /// </returns>
        protected TResult Wait(TOwner owner, string methodName) {
            if (this.owner != owner) {
                throw new ArgumentException(Properties.Resources
                    .ArgumentException_WrongObject);
            }

            var old = Interlocked.CompareExchange(ref waitCount, 1, 0);
            if (old != 0) {
                throw new InvalidOperationException(string.Format(
                    Properties.Resources.InvalidOperation_MultipleCalls,
                    methodName));
            }

            if (!this.completed) {
                this.AsyncWaitHandle.WaitOne();
            }

            if (this.exception != null) {
                throw this.exception;
            } else {
                return this.result;
            }
        }

        /// <summary>
        ///     ThreadPool callback.
        /// </summary>
        private void UserCallback(object state) {
            this.callback(this);
        }
    }
}
