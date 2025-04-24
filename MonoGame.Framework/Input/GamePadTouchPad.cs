// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

namespace Microsoft.Xna.Framework.Input
{
    /// <summary>
    /// A struct that countains information on the left and the right trigger buttons.
    /// </summary>
    public struct GamePadTouchPad
    {
        /// <summary>
        /// Gets the state of the fingers on the touchpad.
        /// </summary>
        /// <value>An array of values indicating the state and position of the finger on the touchpad.</value>
        public FingerState[] Fingers { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Microsoft.Xna.Framework.Input.GamePadTriggers"/> struct.
        /// </summary>
        /// <param name="fingers">The state of the fingers on the touchpad.</param>
        public GamePadTouchPad(FingerState[] fingers) : this()
        {
            Fingers = fingers;
        }

        /// <summary>
        /// Determines whether two specified instances of <see cref="GamePadTouchPad"/> are equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(GamePadTouchPad left, GamePadTouchPad right)
        {
            if (left.Fingers.Length != right.Fingers.Length)
                return false;

            for (int i = 0; i < left.Fingers.Length; ++i)
                if (left.Fingers[i] != right.Fingers[i])
                    return false;

            return true;
        }

        /// <summary>
        /// Determines whether two specified instances of <see cref="GamePadTouchPad"/> are not equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(GamePadTouchPad left, GamePadTouchPad right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns a value indicating whether this instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">An object to compare to this instance.</param>
        /// <returns>true if <paramref name="obj"/> is a <see cref="GamePadTouchPad"/> and has the same value as this instance; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return (obj is GamePadTouchPad) && (this == (GamePadTouchPad)obj);
        }

        /// <summary>
        /// Serves as a hash function for a <see cref="T:Microsoft.Xna.Framework.Input.GamePadTouchPad"/> object.
        /// </summary>
        /// <returns>A hash code for this instance that is suitable for use in hashing algorithms and data structures such as a
        /// hash table.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int result = 0;

                for (int i = 0; i < Fingers.Length; ++i)
                    result = (result * 397) ^ Fingers[i].GetHashCode();

                return result;
            }
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:Microsoft.Xna.Framework.Input.GamePadTouchPad"/>.
        /// </summary>
        /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:Microsoft.Xna.Framework.Input.GamePadTouchPad"/>.</returns>
        public override string ToString()
        {
            string fingers = "";
            if (Fingers.Length > 0)
            {
                fingers = Fingers[0].ToString();
                for (int i = 1; i < Fingers.Length; ++i)
                    fingers += $", {Fingers[i]}";
            }
            return "[GamePadTouchPad: Fingers=[" + fingers + "]]";
        }
    }
}
