using Microsoft.Win32;
using System;
using System.IO;
using System.Security;

namespace TestIntelReporter {
    /// <summary>
    ///     Manages the storage of program settings in the registry.
    /// </summary>
    internal class Settings {
        /// <summary>
        ///     There is very little specific to us, so keep the services
        ///     information on a shared TEST registry key.
        /// </summary>
        private string KeyName = "SOFTWARE\\Test Alliance Please Ignore";
        /// <summary>
        ///     The user's TEST authentication name.
        /// </summary>
        private string UsernameKey = "Username";
        /// <summary>
        ///     The user's TEST services password pre-hashed.
        /// </summary>
        private string PasswordKey = "ServicesPasswordHash";

        /// <summary>
        ///     Initializes a new instance of the <see cref="Settings"/>
        ///     class.
        /// </summary>
        /// <remarks>
        ///     Attempts to read out the values of keys already in the
        ///     registry.
        /// </remarks>
        public Settings() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyName)) {
                    if (key != null) {
                        this.Username = key.GetValue(UsernameKey) as string;
                        this.PasswordHash = key.GetValue(PasswordKey) as string;
                    }
                }
            } catch (SecurityException) {
                // This really shouldn't happen...
            } catch (IOException) {
                // Again, really shouldn't happen...
            } catch (UnauthorizedAccessException) {
                // Nor this one...
            }
        }

        /// <summary>
        ///     Gets or sets the user's AUTH username.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        ///     Gets or sets the SHA1 hash of the user's services password.
        /// </summary>
        public string PasswordHash { get; set; }

        /// <summary>
        ///     Saves the settings back into the registry.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if the settings were successfully saved;
        ///     otherwise, <see langword="false"/>.
        /// </returns>
        public bool Save() {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyName)) {
                    key.SetValue(UsernameKey, this.Username ?? String.Empty);
                    key.SetValue(PasswordKey, this.PasswordHash ?? String.Empty);
                    return true;
                }
            } catch (SecurityException) {
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
            return false;
        }
    }
}
