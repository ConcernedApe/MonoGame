// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using MonoGame.OpenGL;

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed partial class TextureCollection
    {
        private TextureTarget[] _targets;

        void PlatformInit()
        {
            _targets = new TextureTarget[_textures.Length];
        }

        void PlatformClear()
        {
            for (var i = 0; i < _targets.Length; i++)
                _targets[i] = 0;
        }

        void PlatformSetTextures(GraphicsDevice device)
        {
            // Skip out if nothing has changed.
            if (_dirty == 0)
                return;

            for (var i = 0; i < _textures.Length; i++)
            {
                var mask = 1 << i;
                if ((_dirty & mask) == 0)
                    continue;

                var tex = _textures[i];

                TextureTarget? bindTarget = null;
                int bindTexture = 0;

                // Clear the previous binding if the 
                // target is different from the new one.
                if (_targets[i] != 0 && (tex == null || _targets[i] != tex.glTarget))
                {
                    bindTarget = _targets[i];
                    bindTexture = 0;
                    _targets[i] = 0;
                }

                if (tex != null)
                {
                    _targets[i] = tex.glTarget;
                    bindTarget = tex.glTarget;
                    bindTexture = tex.glTexture;

                    unchecked
                    {
                        _graphicsDevice._graphicsMetrics._textureCount++;
                    }
                }

                if (bindTarget.HasValue)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GraphicsExtensions.CheckGLError();

                    GL.BindTexture(bindTarget.Value, bindTexture);
                    GraphicsExtensions.CheckGLError();
                }

                _dirty &= ~mask;
                if (_dirty == 0)
                    break;
            }

            _dirty = 0;
        }
    }
}
