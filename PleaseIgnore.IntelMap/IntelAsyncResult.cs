using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Implementation of <see cref="IAsyncResult"/>, providing common
    ///     support for asynchronous function calls in PleaseIgnore.
    /// </summary>
    /// <typeparam name="TResult">
    ///     The return <see cref="Type"/> of the <c>End<var>Name</var></c>
    ///     call.
    /// </typeparam>
    internal class IntelAsyncResult<TResult> : IAsyncResult {
        // The callback to call when execution has completed
        private readonly AsyncCallback callback;
        // The value of AsyncState
        [ContractPublicPropertyName("AsyncState")]
        private readonly object state;
        // Number of waits made on End*()
        private int waitCount;
        // Set to true once the asynchronous execution has completed
        [ContractPublicPropertyName("IsCompleted")]
        private bool completed;
        // Set to true if the execution was completed synchronously
        [ContractPublicPropertyName("CompletedSynchronously")]
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
        /// <param name="callback">
        ///     The <see cref="AsyncCallback"/> to call when the asynchronous
        ///     operation completes.  This can be <see langword="null"/>.
        /// </param>
        /// <param name="state">
        ///     The value of <see cref="AsyncState"/>.  This can be
        ///     <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="owner"/> is <see langword="null"/>.
        /// </exception>
        protected IntelAsyncResult(AsyncCallback callback, object state) {
            Contract.Ensures(this.AsyncState == state);
            Contract.Ensures(!this.IsCompleted);
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
                Contract.Ensures(Contract.Result<WaitHandle>() != null);

                var handle = new ManualResetEvent(false);
                var oldhandle = Interlocked.CompareExchange(
                    ref this.waitHandle,
                    handle,
                    null);
                // XXX: CodeContracts doesn't seem to realize that CompareExchange
                // (above) will leave this.waitHandle as not null.
                Contract.Assume(this.waitHandle != null);

                if (this.completed) {
                    this.waitHandle.Set();
                }
                if (oldhandle != null) {
                    // XXX: CompareExchange returns the /original/ value.  If the
                    // original value is not null, it was not replaced, so we need
                    // to delete the new object we created.
                    handle.Close();
                }
                return this.waitHandle;
            }
        }

        /// <summary>
        ///     Signals that the asychronous operation has completed
        ///     successfully.
        /// </summary>
        /// <param name="result">
        ///     The return code for the <c>End*()</c> method.
        /// </param>
        /// <param name="completeSynchronously">
        ///     The value to assign to <see cref="CompletedSynchronously"/>.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be queued onto the
        ///     <see cref="ThreadPool"/>.
        /// </remarks>
        protected void Complete(TResult result, bool completeSynchronously) {
            Contract.Requires<InvalidOperationException>(!this.IsCompleted);
            Contract.Ensures(this.CompletedSynchronously == completeSynchronously);
            Contract.Ensures(this.IsCompleted);

            this.completed = true;
            this.result = result;
            this.synchronous = completeSynchronously;
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
        /// <param name="completeSynchronously">
        ///     The value to assign to <see cref="CompletedSynchronously"/>.
        /// </param>
        /// <remarks>
        ///     If a callback was provided at <see cref="IntelAsyncResult"/>
        ///     initialization, it will be queued onto the
        ///     <see cref="ThreadPool"/>.
        /// </remarks>
        protected void Complete(Exception exception, bool completeSynchronously) {
            Contract.Requires(exception != null);
            Contract.Requires<InvalidOperationException>(!this.IsCompleted);
            Contract.Ensures(this.CompletedSynchronously == completeSynchronously);
            Contract.Ensures(this.IsCompleted);

            this.completed = true;
            this.exception = exception;
            this.synchronous = completeSynchronously;
            Thread.MemoryBarrier();

            if (this.waitHandle != null) {
                this.waitHandle.Set();
            }

            if (callback != null) {
                ThreadPool.QueueUserWorkItem(this.UserCallback);
            }
        }

        /// <summary>
        ///     Waits for the asynchronous call to complete, returning the queued
        ///     value or throwing an exception as appropriate.
        /// </summary>
        /// <param name="methodName">
        ///     The name of the function being used to wait on this
        ///     <see cref="IntelAsyncResult"/>.
        /// </param>
        /// <returns>
        ///     The value provided to <see cref="AsyncComplete(TResult)"/> or
        ///     <see cref="SyncComplete(TResult)"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     <paramref name="methodName"/> is <see langword="null"/> or
        ///     <see cref="String.Empty"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     A call has already been made to <see cref="Wait"/> on this
        ///     instance of <see cref="IntelAsyncResult"/>.
        /// </exception>
        /// <exception cref="Exception">
        ///     Any exception registered by calling
        ///     <see cref="Complete(Exception, bool)"/>.
        /// </exception>
        public TResult Wait(string methodName) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(methodName));
            Contract.Ensures(this.completed);

            var old = Interlocked.CompareExchange(ref waitCount, 1, 0);
            if (old != 0) {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    Properties.Resources.InvalidOperation_MultipleCalls,
                    methodName));
            }

            if (!this.completed) {
                this.AsyncWaitHandle.WaitOne();
                Contract.Assert(this.completed);
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
            Contract.Requires(this.callback != null);
            this.callback(this);
        }
    }
}
