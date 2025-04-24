// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

namespace Microsoft.Xna.Framework.Input
{
    /// <summary>
    /// A struct that countains information on the left and the right trigger buttons.
    /// </summary>
    public struct FingerState
    {
        /// <summary>
        /// Gets a value indicating the position of the finger on the touchpad.
        /// </summary>
        /// <value>A <see cref="Vector2"/> indicating the current position of the finger on the touchpad.</value>
        public Vector2 Position { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the finger is on the touchpad.
        /// </summary>
        /// <value>True if the finger is on the touchpad; otherwise, false.</value>
        public bool Touching { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Microsoft.Xna.Framework.Input.FingerState"/> struct.
        /// </summary>
        /// <param name="position">The position of finger on the touchpad.</param>
        /// <param name="touching">Whether the finger is on the touchpad.</param>
        public FingerState(Vector2 position, bool touching) : this()
        {
            Position = position;
            Touching = touching;
        }

        /// <summary>
        /// Determines whether two specified instances of <see cref="FingerState"/> are equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(FingerState left, FingerState right)
        {
            return (left.Position == right.Position) && (left.Touching == right.Touching);
        }

        /// <summary>
        /// Determines whether two specified instances of <see cref="FingerState"/> are not equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(FingerState left, FingerState right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns a value indicating whether this instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">An object to compare to this instance.</param>
        /// <returns>true if <paramref name="obj"/> is a <see cref="FingerState"/> and has the same value as this instance; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return (obj is FingerState) && (this == (FingerState)obj);
        }

        /// <summary>
        /// Serves as a hash function for a <see cref="T:Microsoft.Xna.Framework.Input.FingerState"/> object.
        /// </summary>
        /// <returns>A hash code for this instance that is suitable for use in hashing algorithms and data structures such as a
        /// hash table.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Position.GetHashCode() * 397) ^ Touching.GetHashCode();
            }
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:Microsoft.Xna.Framework.Input.FingerState"/>.
        /// </summary>
        /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:Microsoft.Xna.Framework.Input.FingerState"/>.</returns>
        public override string ToString()
        {
            return "[FingerState: Position=" + Position + ", Touching=" + Touching + "]";
        }
    }
}
