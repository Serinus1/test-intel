using System;
using System.Security.Cryptography;
using System.Text;

namespace TestIntelReporter {
    public sealed class IntelSession : IDisposable {
        public IntelSession(string username, string passwordHash) {
            var response = WebMethods.Authenticate(username, passwordHash);
            this.Username = username;
            this.Session = response.Item1;
            this.Users = response.Item2;
        }

        public int Users { get; private set; }

        public string Username { get; private set; }

        public string Session { get; private set; }

        public bool IsOpen { get { return Session != null; } }

        public void Dispose() {
            if (Session != null) {
                try {
                    WebMethods.Logoff(Username, Session);
                } catch (IntelException) {
                } finally {
                    Session = null;
                }
            }
        }

        public bool KeepAlive() {
            if (Session != null) {
                try {
                    Users = WebMethods.KeepAlive(Session);
                    return true;
                } catch (IntelSessionException) {
                    Session = null;
                    return false;
                } catch (IntelException) {
                    return false;
                }
            }
            return false;
        }

        public bool Report(DateTime timestamp, string channel, string message) {
            if (Session != null) {
                try {
                    WebMethods.Report(Session, timestamp, channel, message);
                    return true;
                } catch (IntelSessionException) {
                    Session = null;
                    return false;
                } catch (IntelException) {
                    return false;
                }
            }
            return false;
        }

        public static string HashPassword(string password) {
            if (password == null) throw new ArgumentNullException("password");

            var sha = new SHA1CryptoServiceProvider();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}
