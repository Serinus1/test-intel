using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
using System.Linq;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    /// Specialization of <see cref="System.ComponentModel.ComponentCollection" />
    /// providing a list of <see cref="IntelChannel" />.
    /// </summary>
    public class IntelChannelCollection : ReadOnlyCollection<IntelChannel> {
        /// <summary>
        /// Initializes a new instance of <see cref="IntelChannelCollection" />.
        /// </summary>
        /// <param name="list">The list to expose to the user.</param>
        public IntelChannelCollection(IEnumerable<IntelChannel> list)
                : base(list.ToArray()) {
            Contract.Requires<ArgumentNullException>(list != null, "list");
            Contract.Requires<ArgumentException>(Contract.ForAll(list, x => x != null));
        }

        /// <summary>
        /// Gets any <see cref="IntelChannel" /> monitoring the specified
        /// channel.
        /// </summary>
        /// <param name="name">The <see cref="IntelChannel.Name" /> to fetch.</param>
        /// <returns>
        /// An instance of <see cref="IntelChannel" /> with the
        /// <see cref="IntelChannel.Name" /> specified by
        /// <paramref name="name" /> or <see langword="null" /> if
        /// no such channel is being monitored.
        /// </returns>
        public IntelChannel this[string name] {
            get {
                if (name == null) {
                    return null;
                } else {
                    return this.FirstOrDefault(x => String.Equals(
                        x.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }
}
