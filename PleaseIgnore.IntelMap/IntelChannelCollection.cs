using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    /// Specialization of <see cref="System.ComponentModel.ComponentCollection" />
    /// providing a list of <see cref="IntelChannel" />.
    /// </summary>
    public class IntelChannelCollection : ComponentCollection, IList<IntelChannel> {
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
        /// <value>The <see cref="IntelChannel"/> at the specified
        /// index.</value>
        public override IComponent this[string name] {
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

        /// <summary>
        /// Gets the <see cref="IntelChannel"/> in the collection at the
        /// specified collection index.
        /// </summary>
        /// <param name="index">The collection index of the
        /// <see cref="IntelChannel"/> to get.</param>
        /// <value>The <see cref="IntelChannel"/> at the specified
        /// index.</value>
        public new IntelChannel this[int index] {
            get {
                Contract.Requires<ArgumentOutOfRangeException>(
                    (index >= 0) && (index < this.Count), "index");
                return (IntelChannel)base[index];
            }
        }

        /// <summary>Copies the entire collection to an array, starting
        /// writing at the specified array index.</summary>
        /// <param name="array">An <see cref="IntelChannel"/> array to copy
        /// the objects in the collection to.</param>
        /// <param name="arrayIndex">The index of the <paramref name="array"/>
        /// at which copying to should begin.</param>
        public void CopyTo(IntelChannel[] array, int arrayIndex) {
            this.InnerList.CopyTo(array, arrayIndex);
        }

        /// <summary>Determines whether the <see cref="IntelChannelCollection" />
        /// contains a specific value.</summary>
        /// <param name="item">The object to locate in the
        /// <see cref="ICollection{T}" />.</param>
        /// <returns><see langword="true"/> if <paramref name="item" /> is
        /// found in the <see cref="IntelChannelCollection" />; otherwise,
        /// <see langword="false"/>.</returns>
        public bool Contains(IntelChannel item) {
            return this.InnerList.Contains(item);
        }

        /// <summary>Returns an enumerator that iterates through the
        /// collection.</summary>
        /// <returns>An instance of <see cref="IEnumerator{T}" /> that can be
        /// used to iterate through the collection.</returns>
        public new IEnumerator<IntelChannel> GetEnumerator() {
            return this.InnerList.Cast<IntelChannel>().GetEnumerator();
        }

        /// <summary>Determines the index of a specific item in the
        /// <see cref="IntelChannelCollection" />.</summary>
        /// <param name="item">The object to locate in the
        /// <see cref="IntelChannelCollection" />.</param>
        /// <returns>The index of <paramref name="item" /> if found in the
        /// list; otherwise, -1.</returns>
        public int IndexOf(IntelChannel item) {
            return this.InnerList.IndexOf(item);
        }

        /// <summary>Gets the <see cref="IntelChannel" /> in the collection
        /// at the specified collection index.</summary>
        /// <param name="index">he collection index of the
        /// <see cref="IntelChannel"/> to get.</param>
        /// <returns>The <see cref="IntelChannel"/> at the specified
        /// index.</returns>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        IntelChannel IList<IntelChannel>.this[int index] {
            get { return (IntelChannel)base[index]; }
            set { throw new NotSupportedException(); }
        }

        /// <summary>Gets a value indicating whether the
        /// <see cref="ICollection{T}" /> is read-only.</summary>
        /// <returns><see langword="true"/> if the <see cref="ICollection{T}" />
        /// is read-only; otherwise, <see langword="false"/>.</returns>
        bool ICollection<IntelChannel>.IsReadOnly { get { return true; } }

        /// <summary>Gets the number of elements contained in the
        /// <see cref="ICollection{T}" /> instance.</summary>
        /// <returns>The number of elements contained in the
        /// <see cref="ICollection{T}" /> instance.</returns>
        int ICollection<IntelChannel>.Count { get { return this.Count; } }

        /// <summary>Adds an item to the <see cref="ICollection{T}" />.</summary>
        /// <param name="item">The object to add to the
        /// <see cref="ICollection{T}" />.</param>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        void ICollection<IntelChannel>.Add(IntelChannel item) {
            throw new NotSupportedException();
        }

        /// <summary>Removes all items from the
        /// <see cref="ICollection{T}" />.</summary>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        void ICollection<IntelChannel>.Clear() {
            throw new NotSupportedException();
        }

        /// <summary>Inserts an item to the <see cref="IList{T}" /> at the
        /// specified index.</summary>
        /// <param name="item">The object to insert into the
        /// <see cref="IList{T}" />.</param>
        /// <param name="index">The zero-based index at which
        /// <paramref name="item" /> should be inserted.</param>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        void IList<IntelChannel>.Insert(int index, IntelChannel item) {
            throw new NotSupportedException();
        }

        /// <summary>Removes the first occurrence of a specific object from the
        /// <see cref="ICollection{T}" />.</summary>
        /// <param name="item">The object to remove from the
        /// <see cref="ICollection{T}" />.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="item" /> was successfully
        /// removed from the <see cref="ICollection{T}" />; otherwise,
        /// <see langword="false"/>.  This method also returns <see langword="false"/>
        /// if <paramref name="item" /> is not found in the original
        /// <see cref="ICollection{T}" />.
        /// </returns>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        bool ICollection<IntelChannel>.Remove(IntelChannel item) {
            throw new NotSupportedException();
        }

        /// <summary>Removes the <see cref="IntelChannel" /> item at the
        /// specified index.</summary>
        /// <param name="index">The zero-based index of the item to
        /// remove.</param>
        /// <exception cref="NotSupportedException"><see cref="IntelChannelCollection"/>
        /// is read only.</exception>
        void IList<IntelChannel>.RemoveAt(int index) {
            throw new NotSupportedException();
        }
    }
}
