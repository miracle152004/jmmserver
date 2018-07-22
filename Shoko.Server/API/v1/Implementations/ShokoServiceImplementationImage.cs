﻿using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using Shoko.Models;
using Shoko.Models.Server;
using Shoko.Server.Repositories.Direct;
using NLog;
using Nancy;
using Nancy.Rest.Module;
using Shoko.Commons.Properties;
using Shoko.Models.Enums;
using Shoko.Models.Interfaces;
using Shoko.Server.Models;
using Shoko.Server.Extensions;
using Shoko.Server.Properties;
using Shoko.Server.Repositories;
using Resources = Shoko.Server.Properties.Resources;

namespace Shoko.Server
{
    public class ShokoServiceImplementationImage : IShokoServerImage
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public Stream GetImage(int imageId, int imageType, bool? thumnbnailOnly)
        {
            string path = GetImagePath(imageId, imageType, thumnbnailOnly);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new StreamWithResponse(HttpStatusCode.NotFound);
            return new StreamWithResponse(File.OpenRead(path), MimeTypes.GetMimeType(path));
        }

        public Stream GetImageUsingPath(string serverImagePath)
        {
            if (File.Exists(serverImagePath))
            {
                return new StreamWithResponse(File.OpenRead(serverImagePath), MimeTypes.GetMimeType(serverImagePath));
            }
            logger.Trace("Could not find AniDB_Cover image: {0}", serverImagePath);
            return new StreamWithResponse(HttpStatusCode.NotFound);
        }

        public Stream BlankImage()
        {
            byte[] dta = Resources.blank;
            MemoryStream ms = new MemoryStream(dta);
            ms.Seek(0, SeekOrigin.Begin);
            return new StreamWithResponse(ms, "image/jpeg");
        }

        internal static Image ReSize(Image im, int width, int height)
        {
            Bitmap dest = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(dest))
            {
                g.InterpolationMode = width >= im.Width
                    ? InterpolationMode.HighQualityBilinear
                    : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(im, 0, 0, width, height);
            }
            return dest;
        }

        public Stream ResizeToRatio(Image im, double newratio)
        {
            double calcwidth = im.Width;
            double calcheight = im.Height;

            if (Math.Abs(newratio) < 0.001D)
            {
                MemoryStream stream = new MemoryStream();
                im.Save(stream, ImageFormat.Jpeg);
                stream.Seek(0, SeekOrigin.Begin);
                return new StreamWithResponse(stream, "image/jpg");
            }

            double nheight = 0;
            do
            {
                nheight = calcwidth / newratio;
                if (nheight > im.Height + 0.5F)
                {
                    calcwidth = calcwidth * (im.Height / nheight);
                }
                else
                {
                    calcheight = nheight;
                }
            } while (nheight > im.Height + 0.5F);

            int newwidth = (int) Math.Round(calcwidth);
            int newheight = (int) Math.Round(calcheight);
            int x = 0;
            int y = 0;
            if (newwidth < im.Width)
                x = (im.Width - newwidth) / 2;
            if (newheight < im.Height)
                y = (im.Height - newheight) / 2;

            Image im2 = ReSize(im, newwidth, newheight);
            Graphics g = Graphics.FromImage(im2);
            g.DrawImage(im, new Rectangle(0, 0, im2.Width, im2.Height),
                new Rectangle(x, y, im2.Width, im2.Height), GraphicsUnit.Pixel);
            MemoryStream ms = new MemoryStream();
            im2.Save(ms, ImageFormat.Jpeg);
            ms.Seek(0, SeekOrigin.Begin);
            return new StreamWithResponse(ms, "image/jpg");
        }

        public System.IO.Stream GetSupportImage(string name, float? ratio)
        {
            if (string.IsNullOrEmpty(name))
                return new MemoryStream();
            
            name = Path.GetFileNameWithoutExtension(name);
            System.Resources.ResourceManager man = Resources.ResourceManager;
            byte[] dta = (byte[]) man.GetObject(name);
            if ((dta == null) || (dta.Length == 0))
                return new MemoryStream();
            
            //Little hack
            MemoryStream ms = new MemoryStream(dta);
            ms.Seek(0, SeekOrigin.Begin);
            if (!name.Contains("404") || (ratio == null || Math.Abs(ratio.Value) < 0.001D))
                return new StreamWithResponse(ms, "image/png");
            
            Image im = Image.FromStream(ms);
            float w = im.Width;
            float h = im.Height;
            float nw;
            float nh;

            if (w <= h)
            {
                nw = h * ratio.Value;
                if (nw < w)
                {
                    nw = w;
                    nh = w / ratio.Value;
                }
                else
                    nh = h;
            }
            else
            {
                nh = w / ratio.Value;
                if (nh < h)
                {
                    nh = h;
                    nw = w * ratio.Value;
                }
                else
                    nw = w;
            }
            nw = (float) Math.Round(nw);
            nh = (float) Math.Round(nh);
            Image im2 = new Bitmap((int) nw, (int) nh, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(im2))
            {
                g.InterpolationMode = nw >= im.Width
                    ? InterpolationMode.HighQualityBilinear
                    : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                Rectangle src = new Rectangle(0, 0, im.Width, im.Height);
                Rectangle dst = new Rectangle((int) ((nw - w) / 2), (int) ((nh - h) / 2), im.Width, im.Height);
                g.DrawImage(im, dst, src, GraphicsUnit.Pixel);
            }
            MemoryStream ms2 = new MemoryStream();
            im2.Save(ms2, ImageFormat.Png);
            ms2.Seek(0, SeekOrigin.Begin);
            ms.Dispose();
            return new StreamWithResponse(ms2, "image/png");
        }

        public System.IO.Stream GetThumb(int imageId, int imageType, float ratio)
        {
            using (Stream m = GetImage(imageId, imageType, false))
            {
                if (m == null) return new StreamWithResponse(HttpStatusCode.NotFound);
                using (Image im = Image.FromStream(m))
                {
                    return ResizeToRatio(im, ratio);
                }
            }
        }

        public string GetImagePath(int imageId, int imageType, bool? thumnbnailOnly)
        {
            ImageEntityType it = (ImageEntityType) imageType;

            switch (it)
            {
                case ImageEntityType.AniDB_Cover:
                    SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(imageId);
                    if (anime == null) return null;
                    if (File.Exists(anime.PosterPath))
                    {
                        return anime.PosterPath;
                    }
                    else
                    {
                        logger.Trace("Could not find AniDB_Cover image: {0}", anime.PosterPath);
                        return string.Empty;
                    }

                case ImageEntityType.AniDB_Character:
                    AniDB_Character chr = RepoFactory.AniDB_Character.GetByID(imageId);
                    if (chr == null) return null;
                    if (File.Exists(chr.GetPosterPath()))
                    {
                        return chr.GetPosterPath();
                    }
                    else
                    {
                        logger.Trace("Could not find AniDB_Character image: {0}", chr.GetPosterPath());
                        return string.Empty;
                    }

                case ImageEntityType.AniDB_Creator:
                    AniDB_Seiyuu creator = RepoFactory.AniDB_Seiyuu.GetByID(imageId);
                    if (creator == null) return string.Empty;
                    if (File.Exists(creator.GetPosterPath()))
                    {
                        return creator.GetPosterPath();
                    }
                    else
                    {
                        logger.Trace("Could not find AniDB_Creator image: {0}", creator.GetPosterPath());
                        return string.Empty;
                    }

                case ImageEntityType.TvDB_Cover:
                    TvDB_ImagePoster poster = RepoFactory.TvDB_ImagePoster.GetByID(imageId);
                    if (poster == null) return null;
                    if (File.Exists(poster.GetFullImagePath()))
                    {
                        return poster.GetFullImagePath();
                    }
                    else
                    {
                        logger.Trace("Could not find TvDB_Cover image: {0}", poster.GetFullImagePath());
                        return string.Empty;
                    }

                case ImageEntityType.TvDB_Banner:
                    TvDB_ImageWideBanner wideBanner = RepoFactory.TvDB_ImageWideBanner.GetByID(imageId);
                    if (wideBanner == null) return null;
                    if (File.Exists(wideBanner.GetFullImagePath()))
                    {
                        return wideBanner.GetFullImagePath();
                    }
                    else
                    {
                        logger.Trace("Could not find TvDB_Banner image: {0}", wideBanner.GetFullImagePath());
                        return string.Empty;
                    }

                case ImageEntityType.TvDB_Episode:
                    TvDB_Episode ep = RepoFactory.TvDB_Episode.GetByID(imageId);
                    if (ep == null) return null;
                    if (File.Exists(ep.GetFullImagePath()))
                    {
                        return ep.GetFullImagePath();
                    }
                    else
                    {
                        logger.Trace("Could not find TvDB_Episode image: {0}", ep.GetFullImagePath());
                        return string.Empty;
                    }

                case ImageEntityType.TvDB_FanArt:
                    TvDB_ImageFanart fanart = RepoFactory.TvDB_ImageFanart.GetByID(imageId);
                    if (fanart == null) return null;
                    if (thumnbnailOnly.HasValue && thumnbnailOnly.Value)
                    {
                        if (File.Exists(fanart.GetFullThumbnailPath()))
                        {
                            return fanart.GetFullThumbnailPath();
                        }
                        
                        logger.Trace("Could not find TvDB_FanArt image: {0}", fanart.GetFullThumbnailPath());
                        return string.Empty;
                    }
                    else
                    {
                        if (File.Exists(fanart.GetFullImagePath()))
                        {
                            return fanart.GetFullImagePath();
                        }
                        
                        logger.Trace("Could not find TvDB_FanArt image: {0}", fanart.GetFullImagePath());
                        return string.Empty;
                    }

                case ImageEntityType.MovieDB_Poster:
                    MovieDB_Poster mPoster = RepoFactory.MovieDB_Poster.GetByID(imageId);
                    if (mPoster == null) return null;

                    // now find only the original size
                    mPoster = RepoFactory.MovieDB_Poster.GetByOnlineID(mPoster.URL);
                    if (mPoster == null) return null;
                    if (File.Exists(mPoster.GetFullImagePath()))
                    {
                        return mPoster.GetFullImagePath();
                    }
                    else
                    {
                        logger.Trace("Could not find MovieDB_Poster image: {0}", mPoster.GetFullImagePath());
                        return string.Empty;
                    }

                case ImageEntityType.MovieDB_FanArt:
                    MovieDB_Fanart mFanart = RepoFactory.MovieDB_Fanart.GetByID(imageId);
                    if (mFanart == null) return null;
                    mFanart = RepoFactory.MovieDB_Fanart.GetByOnlineID(mFanart.URL);
                    if (mFanart == null) return null;
                    if (File.Exists(mFanart.GetFullImagePath()))
                    {
                        return mFanart.GetFullImagePath();
                    }
                    else
                    {
                        logger.Trace("Could not find MovieDB_FanArt image: {0}", mFanart.GetFullImagePath());
                        return string.Empty;
                    }

                default:
                    return string.Empty;
            }
        }
    }
}