﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using AniDBAPI;
using NLog;
using Shoko.Commons.Extensions;
using Shoko.Commons.Utils;
using Shoko.Models.Azure;
using Shoko.Models.Client;
using Shoko.Models.Enums;
using Shoko.Models.Server;
using Shoko.Server.Commands;
using Shoko.Server.Databases;
using Shoko.Server.Extensions;
using Shoko.Server.ImageDownload;
using Shoko.Server.LZ4;
using Shoko.Server.Repositories;
using Shoko.Server.Repositories.Repos;
using Shoko.Server.Tasks;

namespace Shoko.Server.Models
{
    public class SVR_AniDB_Anime : AniDB_Anime
    {
        #region DB columns

        public int ContractVersion { get; set; }
        public byte[] ContractBlob { get; set; }
        public int ContractSize { get; set; }

        public const int CONTRACT_VERSION = 7;

        #endregion

        #region Properties and fields

        [NotMapped]
        private CL_AniDB_AnimeDetailed _contract;
        [NotMapped]
        public virtual CL_AniDB_AnimeDetailed Contract
        {
            get
            {
                if ((_contract == null) && (ContractBlob != null) && (ContractBlob.Length > 0) && (ContractSize > 0))
                    _contract = CompressionHelper.DeserializeObject<CL_AniDB_AnimeDetailed>(ContractBlob,
                        ContractSize);
                return _contract;
            }
            set
            {
                _contract = value;
                ContractBlob = CompressionHelper.SerializeObject(value, out int outsize);
                ContractSize = outsize;
                ContractVersion = CONTRACT_VERSION;
            }
        }

        public void CollectContractMemory()
        {
            _contract = null;
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        // these files come from AniDB but we don't directly save them
        private string reviewIDListRAW;

        [XmlIgnore]
        public string PosterPath
        {
            get
            {
                if (string.IsNullOrEmpty(Picname)) return string.Empty;

                return Path.Combine(ImageUtils.GetAniDBImagePath(AnimeID), Picname);
            }
        }

        public static void GetRelatedAnimeRecursive(int animeID,
            ref List<SVR_AniDB_Anime> relList,
            ref List<int> relListIDs, ref List<int> searchedIDs)
        {
            SVR_AniDB_Anime anime = Repo.AniDB_Anime.GetByAnimeID(animeID);
            searchedIDs.Add(animeID);

            foreach (AniDB_Anime_Relation rel in anime.GetRelatedAnime())
            {
                string relationtype = rel.RelationType.ToLower();
                if (SVR_AnimeGroup.IsRelationTypeInExclusions(relationtype))
                {
                    //Filter these relations these will fix messes, like Gundam , Clamp, etc.
                    continue;
                }
                SVR_AniDB_Anime relAnime = Repo.AniDB_Anime.GetByAnimeID(rel.RelatedAnimeID);
                if (relAnime != null && !relListIDs.Contains(relAnime.AnimeID))
                {
                    if (SVR_AnimeGroup.IsRelationTypeInExclusions(relAnime.GetAnimeTypeDescription().ToLower()))
                        continue;
                    relList.Add(relAnime);
                    relListIDs.Add(relAnime.AnimeID);
                    if (!searchedIDs.Contains(rel.RelatedAnimeID))
                    {
                        GetRelatedAnimeRecursive(rel.RelatedAnimeID, ref relList, ref relListIDs,
                            ref searchedIDs);
                    }
                }
            }
        }
        public List<TvDB_Episode> GetTvDBEpisodes()
        {
            List<TvDB_Episode> results = new List<TvDB_Episode>();
            int id = GetCrossRefTvDB()?.FirstOrDefault()?.TvDBID ?? -1;
            if (id != -1)
                results.AddRange(Repo.TvDB_Episode.GetBySeriesID(id).OrderBy(a => a.SeasonNumber)
                    .ThenBy(a => a.EpisodeNumber));
            return results;
        }

        private Dictionary<int, TvDB_Episode> dictTvDBEpisodes;
        public Dictionary<int, TvDB_Episode> GetDictTvDBEpisodes()
        {
            if (dictTvDBEpisodes == null)
            {
                try
                {
                    List<TvDB_Episode> tvdbEpisodes = GetTvDBEpisodes();
                    if (tvdbEpisodes != null)
                    {
                        dictTvDBEpisodes = new Dictionary<int, TvDB_Episode>();
                        // create a dictionary of absolute episode numbers for tvdb episodes
                        // sort by season and episode number
                        // ignore season 0, which is used for specials

                        int i = 1;
                        foreach (TvDB_Episode ep in tvdbEpisodes)
                        {
                            dictTvDBEpisodes[i] = ep;
                            i++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, ex.ToString());
                }
            }
            return dictTvDBEpisodes;
        }

        private Dictionary<int, int> dictTvDBSeasons;
        public Dictionary<int, int> GetDictTvDBSeasons()
        {
            if (dictTvDBSeasons == null)
            {
                try
                {
                    dictTvDBSeasons = new Dictionary<int, int>();
                    // create a dictionary of season numbers and the first episode for that season
                    int i = 1;
                    int lastSeason = -999;
                    foreach (TvDB_Episode ep in GetTvDBEpisodes())
                    {
                        if (ep.SeasonNumber != lastSeason)
                            dictTvDBSeasons[ep.SeasonNumber] = i;

                        lastSeason = ep.SeasonNumber;
                        i++;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, ex.ToString());
                }
            }
            return dictTvDBSeasons;
        }

        private Dictionary<int, int> dictTvDBSeasonsSpecials;
        public Dictionary<int, int> GetDictTvDBSeasonsSpecials()
        {
            if (dictTvDBSeasonsSpecials == null)
            {
                try
                {
                    dictTvDBSeasonsSpecials = new Dictionary<int, int>();
                    // create a dictionary of season numbers and the first episode for that season
                    int i = 1;
                    int lastSeason = -999;
                    foreach (TvDB_Episode ep in GetTvDBEpisodes())
                    {
                        if (ep.SeasonNumber > 0) continue;

                        int thisSeason = 0;
                        if (ep.AirsBeforeSeason.HasValue) thisSeason = ep.AirsBeforeSeason.Value;
                        if (ep.AirsAfterSeason.HasValue) thisSeason = ep.AirsAfterSeason.Value;

                        if (thisSeason != lastSeason)
                            dictTvDBSeasonsSpecials[thisSeason] = i;

                        lastSeason = thisSeason;
                        i++;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, ex.ToString());
                }
            }
            return dictTvDBSeasonsSpecials;
        }

        public List<CrossRef_AniDB_TvDB_Episode_Override> GetCrossRefTvDBEpisodes() => Repo.CrossRef_AniDB_TvDB_Episode_Override.GetByAnimeID(AnimeID);

        public List<CrossRef_AniDB_TvDB> GetCrossRefTvDB() => Repo.CrossRef_AniDB_TvDB.GetByAnimeID(AnimeID);

        public List<CrossRef_AniDB_TraktV2> GetCrossRefTraktV2() => Repo.CrossRef_AniDB_TraktV2.GetByAnimeID(AnimeID);

        public List<CrossRef_AniDB_MAL> GetCrossRefMAL() => Repo.CrossRef_AniDB_MAL.GetByAnimeID(AnimeID);

        public TvDB_Series GetTvDBSeries()
        {
            int id = GetCrossRefTvDB()?.FirstOrDefault()?.TvDBID ?? -1;
            if (id == -1) return null;
            return Repo.TvDB_Series.GetByTvDBID(id);
        }

        public List<TvDB_ImageFanart> GetTvDBImageFanarts()
        {
            List<TvDB_ImageFanart> results = new List<TvDB_ImageFanart>();
            int id = GetCrossRefTvDB()?.FirstOrDefault()?.TvDBID ?? -1;
            if (id != -1)
                results.AddRange(Repo.TvDB_ImageFanart.GetBySeriesID(id));
            return results;
        }

        public List<TvDB_ImagePoster> GetTvDBImagePosters()
        {
            List<TvDB_ImagePoster> results = new List<TvDB_ImagePoster>();
            int id = GetCrossRefTvDB()?.FirstOrDefault()?.TvDBID ?? -1;
            if (id != -1)
                results.AddRange(Repo.TvDB_ImagePoster.GetBySeriesID(id));
            return results;
        }

        public List<TvDB_ImageWideBanner> GetTvDBImageWideBanners()
        {
            List<TvDB_ImageWideBanner> results = new List<TvDB_ImageWideBanner>();
            int id = GetCrossRefTvDB()?.FirstOrDefault()?.TvDBID ?? -1;
            if (id != -1)
                results.AddRange(Repo.TvDB_ImageWideBanner.GetBySeriesID(id));
            return results;
        }

        public CrossRef_AniDB_Other GetCrossRefMovieDB() => Repo.CrossRef_AniDB_Other.GetByAnimeIDAndType(AnimeID,
            CrossRefType.MovieDB);

        public MovieDB_Movie GetMovieDBMovie()
        {
            CrossRef_AniDB_Other xref = GetCrossRefMovieDB();
            if (xref == null) return null;
            return Repo.MovieDb_Movie.GetByOnlineID(int.Parse(xref.CrossRefID));
        }

        public List<MovieDB_Fanart> GetMovieDBFanarts()
        {
            CrossRef_AniDB_Other xref = GetCrossRefMovieDB();
            if (xref == null) return new List<MovieDB_Fanart>();

            return Repo.MovieDB_Fanart.GetByMovieID(int.Parse(xref.CrossRefID));
        }

        public List<MovieDB_Poster> GetMovieDBPosters()
        {
            CrossRef_AniDB_Other xref = GetCrossRefMovieDB();
            if (xref == null) return new List<MovieDB_Poster>();

            return Repo.MovieDB_Poster.GetByMovieID(int.Parse(xref.CrossRefID));
        }

        public AniDB_Anime_DefaultImage GetDefaultPoster() =>
            Repo.AniDB_Anime_DefaultImage.GetByAnimeIDAndImagezSizeType(AnimeID, (int)ImageSizeType.Poster);

        public string PosterPathNoDefault
        {
            get
            {
                string fileName = Path.Combine(ImageUtils.GetAniDBImagePath(AnimeID), Picname);
                return fileName;
            }
        }

        private List<AniDB_Anime_DefaultImage> allPosters;
        public List<AniDB_Anime_DefaultImage> AllPosters
        {
            get
            {
                if (allPosters != null) return allPosters;
                var posters = new List<AniDB_Anime_DefaultImage>();
                posters.Add(new AniDB_Anime_DefaultImage
                {
                    AniDB_Anime_DefaultImageID = AnimeID,
                    ImageType = (int)ImageEntityType.AniDB_Cover
                });
                var tvdbposters = GetTvDBImagePosters()?.Where(img => img != null).Select(img => new AniDB_Anime_DefaultImage
                {
                    AniDB_Anime_DefaultImageID = img.TvDB_ImagePosterID,
                    ImageType = (int)ImageEntityType.TvDB_Cover
                });
                if (tvdbposters != null) posters.AddRange(tvdbposters);

                var moviebposters = GetMovieDBPosters()?.Where(img => img != null).Select(img => new AniDB_Anime_DefaultImage
                {
                    AniDB_Anime_DefaultImageID = img.MovieDB_PosterID,
                    ImageType = (int)ImageEntityType.MovieDB_Poster
                });
                if (moviebposters != null) posters.AddRange(moviebposters);

                allPosters = posters;
                return posters;
            }
        }

        public string GetDefaultPosterPathNoBlanks()
        {
            AniDB_Anime_DefaultImage defaultPoster = GetDefaultPoster();
            if (defaultPoster == null)
                return PosterPathNoDefault;
            ImageEntityType imageType = (ImageEntityType)defaultPoster.ImageParentType;

            switch (imageType)
            {
                case ImageEntityType.AniDB_Cover:
                    return PosterPath;

                case ImageEntityType.TvDB_Cover:
                    TvDB_ImagePoster tvPoster =
                        Repo.TvDB_ImagePoster.GetByID(defaultPoster.ImageParentID);
                    if (tvPoster != null)
                        return tvPoster.GetFullImagePath();
                    else
                        return PosterPath;

                case ImageEntityType.MovieDB_Poster:
                    MovieDB_Poster moviePoster =
                        Repo.MovieDB_Poster.GetByID(defaultPoster.ImageParentID);
                    if (moviePoster != null)
                        return moviePoster.GetFullImagePath();
                    else
                        return PosterPath;
            }

            return PosterPath;
        }

        public ImageDetails GetDefaultPosterDetailsNoBlanks()
        {
            ImageDetails details = new ImageDetails { ImageType = ImageEntityType.AniDB_Cover, ImageID = AnimeID };
            AniDB_Anime_DefaultImage defaultPoster = GetDefaultPoster();

            if (defaultPoster == null)
                return details;
            ImageEntityType imageType = (ImageEntityType)defaultPoster.ImageParentType;

            switch (imageType)
            {
                case ImageEntityType.AniDB_Cover:
                    return details;

                case ImageEntityType.TvDB_Cover:
                    TvDB_ImagePoster tvPoster =
                        Repo.TvDB_ImagePoster.GetByID(defaultPoster.ImageParentID);
                    if (tvPoster != null)
                        details = new ImageDetails
                        {
                            ImageType = ImageEntityType.TvDB_Cover,
                            ImageID = tvPoster.TvDB_ImagePosterID
                        };
                    return details;

                case ImageEntityType.MovieDB_Poster:
                    MovieDB_Poster moviePoster =
                        Repo.MovieDB_Poster.GetByID(defaultPoster.ImageParentID);
                    if (moviePoster != null)
                        details = new ImageDetails
                        {
                            ImageType = ImageEntityType.MovieDB_Poster,
                            ImageID = moviePoster.MovieDB_PosterID
                        };
                    return details;
            }

            return details;
        }

        public AniDB_Anime_DefaultImage GetDefaultFanart() =>
            Repo.AniDB_Anime_DefaultImage.GetByAnimeIDAndImagezSizeType(AnimeID, (int)ImageSizeType.Fanart);

        public ImageDetails GetDefaultFanartDetailsNoBlanks()
        {
            Random fanartRandom = new Random();

            ImageDetails details = null;
            AniDB_Anime_DefaultImage fanart = GetDefaultFanart();
            if (fanart == null)
            {
                List<CL_AniDB_Anime_DefaultImage> fanarts = Contract.AniDBAnime.Fanarts;
                if (fanarts == null || fanarts.Count == 0) return null;
                CL_AniDB_Anime_DefaultImage art = fanarts[fanartRandom.Next(0, fanarts.Count)];
                details = new ImageDetails
                {
                    ImageID = art.AniDB_Anime_DefaultImageID,
                    ImageType = (ImageEntityType)art.ImageType
                };
                return details;
            }

            ImageEntityType imageType = (ImageEntityType)fanart.ImageParentType;

            switch (imageType)
            {
                case ImageEntityType.TvDB_FanArt:
                    TvDB_ImageFanart tvFanart = Repo.TvDB_ImageFanart.GetByID(fanart.ImageParentID);
                    if (tvFanart != null)
                        details = new ImageDetails
                        {
                            ImageType = ImageEntityType.TvDB_FanArt,
                            ImageID = tvFanart.TvDB_ImageFanartID
                        };
                    return details;

                case ImageEntityType.MovieDB_FanArt:
                    MovieDB_Fanart movieFanart = Repo.MovieDB_Fanart.GetByID(fanart.ImageParentID);
                    if (movieFanart != null)
                        details = new ImageDetails
                        {
                            ImageType = ImageEntityType.MovieDB_FanArt,
                            ImageID = movieFanart.MovieDB_FanartID
                        };
                    return details;
            }

            return null;
        }

        public string GetDefaultFanartOnlineURL()
        {
            Random fanartRandom = new Random();


            if (GetDefaultFanart() == null)
            {
                // get a random fanart
                if (this.GetAnimeTypeEnum() == Shoko.Models.Enums.AnimeType.Movie)
                {
                    List<MovieDB_Fanart> fanarts = GetMovieDBFanarts();
                    if (fanarts.Count == 0) return string.Empty;

                    MovieDB_Fanart movieFanart = fanarts[fanartRandom.Next(0, fanarts.Count)];
                    return movieFanart.URL;
                }
                else
                {
                    List<TvDB_ImageFanart> fanarts = GetTvDBImageFanarts();
                    if (fanarts.Count == 0) return null;

                    TvDB_ImageFanart tvFanart = fanarts[fanartRandom.Next(0, fanarts.Count)];
                    return string.Format(Constants.URLS.TvDB_Images, tvFanart.BannerPath);
                }
            }

            AniDB_Anime_DefaultImage fanart = GetDefaultFanart();
            ImageEntityType imageType = (ImageEntityType)fanart.ImageParentType;

            switch (imageType)
            {
                case ImageEntityType.TvDB_FanArt:
                    TvDB_ImageFanart tvFanart =
                        Repo.TvDB_ImageFanart.GetByID(fanart.ImageParentID);
                    if (tvFanart != null)
                        return string.Format(Constants.URLS.TvDB_Images, tvFanart.BannerPath);
                    break;

                case ImageEntityType.MovieDB_FanArt:
                    MovieDB_Fanart movieFanart =
                        Repo.MovieDB_Fanart.GetByID(fanart.ImageParentID);
                    if (movieFanart != null)
                        return movieFanart.URL;
                    break;
            }

            return string.Empty;
        }

        public AniDB_Anime_DefaultImage GetDefaultWideBanner() =>
            Repo.AniDB_Anime_DefaultImage.GetByAnimeIDAndImagezSizeType(AnimeID, (int)ImageSizeType.WideBanner);

        public ImageDetails GetDefaultWideBannerDetailsNoBlanks()
        {
            Random bannerRandom = new Random();

            ImageDetails details;
            AniDB_Anime_DefaultImage banner = GetDefaultWideBanner();
            if (banner == null)
            {
                // get a random banner (only tvdb)
                if (this.GetAnimeTypeEnum() == Shoko.Models.Enums.AnimeType.Movie)
                {
                    // MovieDB doesn't have banners
                    return null;
                }
                List<CL_AniDB_Anime_DefaultImage> banners = Contract.AniDBAnime.Banners;
                if (banners == null || banners.Count == 0) return null;
                CL_AniDB_Anime_DefaultImage art = banners[bannerRandom.Next(0, banners.Count)];
                details = new ImageDetails
                {
                    ImageID = art.AniDB_Anime_DefaultImageID,
                    ImageType = (ImageEntityType)art.ImageType
                };
                return details;
            }
            ImageEntityType imageType = (ImageEntityType)banner.ImageParentType;

            switch (imageType)
            {
                case ImageEntityType.TvDB_Banner:
                    details = new ImageDetails
                    {
                        ImageType = ImageEntityType.TvDB_Banner,
                        ImageID = banner.ToClient().TVWideBanner.TvDB_ImageWideBannerID
                    };
                    return details;
            }

            return null;
        }


        [XmlIgnore]
        public string TagsString
        {
            get
            {
                List<AniDB_Tag> tags = GetTags();
                string temp = string.Empty;
                foreach (AniDB_Tag tag in tags)
                    temp += tag.TagName + "|";
                if (temp.Length > 2)
                    temp = temp.Substring(0, temp.Length - 2);
                return temp;
            }
        }


        public List<AniDB_Tag> GetTags()
        {
            List<AniDB_Tag> tags = new List<AniDB_Tag>();
            foreach (AniDB_Anime_Tag tag in GetAnimeTags())
            {
                AniDB_Tag newTag = Repo.AniDB_Tag.GetByTagID(tag.TagID);
                if (newTag != null) tags.Add(newTag);
            }
            return tags;
        }

        public List<CustomTag> GetCustomTagsForAnime() => Repo.CustomTag.GetByAnimeID(AnimeID);

        public List<AniDB_Tag> GetAniDBTags() => Repo.AniDB_Tag.GetByAnimeID(AnimeID);

        public List<AniDB_Anime_Tag> GetAnimeTags() => Repo.AniDB_Anime_Tag.GetByAnimeID(AnimeID);

        public List<AniDB_Anime_Relation> GetRelatedAnime() => Repo.AniDB_Anime_Relation.GetByAnimeID(AnimeID);

        public List<AniDB_Anime_Similar> GetSimilarAnime() => Repo.AniDB_Anime_Similar.GetByAnimeID(AnimeID);

        [XmlIgnore]
        public List<AniDB_Anime_Review> AnimeReviews => Repo.AniDB_Anime_Review.GetByAnimeID(AnimeID);

        public List<SVR_AniDB_Anime> GetAllRelatedAnime()
        {
            List<SVR_AniDB_Anime> relList = new List<SVR_AniDB_Anime>();
            List<int> relListIDs = new List<int>();
            List<int> searchedIDs = new List<int>();

            GetRelatedAnimeRecursive(AnimeID, ref relList, ref relListIDs, ref searchedIDs);
            return relList;
        }
        public List<AniDB_Anime_Character> GetAnimeCharacters() => Repo.AniDB_Anime_Character.GetByAnimeID(AnimeID);

        public List<AniDB_Anime_Title> GetTitles() => Repo.AniDB_Anime_Title.GetByAnimeID(AnimeID);

        public string GetFormattedTitle(List<AniDB_Anime_Title> titles)
        {
            foreach (NamingLanguage nlan in Languages.PreferredNamingLanguages)
            {
                string thisLanguage = nlan.Language.Trim().ToUpper();

                // Romaji and English titles will be contained in MAIN and/or OFFICIAL
                // we won't use synonyms for these two languages
                if (thisLanguage.Equals(Shoko.Models.Constants.AniDBLanguageType.Romaji) ||
                    thisLanguage.Equals(Shoko.Models.Constants.AniDBLanguageType.English))
                {
                    foreach (AniDB_Anime_Title title in titles)
                    {
                        string titleType = title.TitleType.Trim().ToUpper();
                        // first try the  Main title
                        if (titleType.Trim().Equals(Shoko.Models.Constants.AnimeTitleType.Main,
                                StringComparison.OrdinalIgnoreCase) &&
                            title.Language.Trim().Equals(thisLanguage, StringComparison.OrdinalIgnoreCase))
                            return title.Title;
                    }
                }

                // now try the official title
                foreach (AniDB_Anime_Title title in titles)
                {
                    string titleType = title.TitleType.Trim();
                    if (titleType.Equals(Shoko.Models.Constants.AnimeTitleType.Official,
                            StringComparison.OrdinalIgnoreCase) &&
                        title.Language.Trim().Equals(thisLanguage, StringComparison.OrdinalIgnoreCase))
                        return title.Title;
                }

                // try synonyms
                if (ServerSettings.Instance.LanguageUseSynonyms)
                {
                    foreach (AniDB_Anime_Title title in titles)
                    {
                        string titleType = title.TitleType.Trim().ToUpper();
                        if (titleType == Shoko.Models.Constants.AnimeTitleType.Synonym.ToUpper() &&
                            title.Language.Trim().ToUpper() == thisLanguage)
                            return title.Title;
                    }
                }
            }

            // otherwise just use the main title
            return MainTitle;
        }

        public string GetFormattedTitle()
        {
            List<AniDB_Anime_Title> thisTitles = GetTitles();
            return GetFormattedTitle(thisTitles);
        }

        [XmlIgnore]
        public AniDB_Vote UserVote
        {
            get
            {
                try
                {
                    return Repo.AniDB_Vote.GetByAnimeID(AnimeID);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error in  UserVote: {ex}");
                    return null;
                }
            }
        }

        public string PreferredTitle => GetFormattedTitle();


        [XmlIgnore]
        public List<AniDB_Episode> AniDBEpisodes => Repo.AniDB_Episode.GetByAnimeID(AnimeID);

        public List<AniDB_Episode> GetAniDBEpisodes() => Repo.AniDB_Episode.GetByAnimeID(AnimeID);

        #endregion

        public SVR_AniDB_Anime()
        {
            DisableExternalLinksFlag = 0;
        }

        #region Init and Populate

        private bool Populate(Raw_AniDB_Anime animeInfo, IAtomic<SVR_AniDB_Anime, object> upd)
        {
            // We need various values to be populated to be considered valid
            if (string.IsNullOrEmpty(animeInfo?.MainTitle) || animeInfo.AnimeID <= 0) return false;
            upd.Entity.AirDate = AirDate = animeInfo.AirDate;
            upd.Entity.AllCinemaID = AllCinemaID = animeInfo.AllCinemaID;
            upd.Entity.AnimeID = AnimeID = animeInfo.AnimeID;
            //this.AnimeNfo = animeInfo.AnimeNfoID;
            upd.Entity.AnimePlanetID = AnimePlanetID = animeInfo.AnimePlanetID;
            upd.Entity.SetAnimeTypeRAW(animeInfo.AnimeTypeRAW); this.SetAnimeTypeRAW(animeInfo.AnimeTypeRAW);
            upd.Entity.ANNID = ANNID = animeInfo.ANNID;
            upd.Entity.AvgReviewRating = AvgReviewRating = animeInfo.AvgReviewRating;
            upd.Entity.AwardList = AwardList = animeInfo.AwardList;
            upd.Entity.BeginYear = BeginYear = animeInfo.BeginYear;

            upd.Entity.DateTimeDescUpdated = DateTimeDescUpdated = DateTime.Now;
            upd.Entity.DateTimeUpdated = DateTimeUpdated = DateTime.Now;

            upd.Entity.Description = Description = animeInfo.Description ?? string.Empty;
            upd.Entity.EndDate = EndDate = animeInfo.EndDate;
            upd.Entity.EndYear = EndYear = animeInfo.EndYear;
            upd.Entity.MainTitle = MainTitle = animeInfo.MainTitle;
            upd.Entity.AllTitles = AllTitles = string.Empty;
            upd.Entity.AllTags = AllTags = string.Empty;
            //this.EnglishName = animeInfo.EnglishName;
            upd.Entity.EpisodeCount = EpisodeCount = animeInfo.EpisodeCount;
            upd.Entity.EpisodeCountNormal = EpisodeCountNormal = animeInfo.EpisodeCountNormal;
            upd.Entity.EpisodeCountSpecial = EpisodeCountSpecial = animeInfo.EpisodeCountSpecial;
            //this.genre
            upd.Entity.ImageEnabled = ImageEnabled = 1;
            //this.KanjiName = animeInfo.KanjiName;
            upd.Entity.LatestEpisodeNumber = LatestEpisodeNumber = animeInfo.LatestEpisodeNumber;
            //this.OtherName = animeInfo.OtherName;
            upd.Entity.Picname = Picname = animeInfo.Picname;
            upd.Entity.Rating = Rating = animeInfo.Rating;
            //this.relations
            upd.Entity.Restricted = Restricted = animeInfo.Restricted;
            upd.Entity.ReviewCount = ReviewCount = animeInfo.ReviewCount;
            //this.RomajiName = animeInfo.RomajiName;
            //this.ShortNames = animeInfo.ShortNames.Replace("'", "|");
            //this.Synonyms = animeInfo.Synonyms.Replace("'", "|");
            upd.Entity.TempRating = TempRating = animeInfo.TempRating;
            upd.Entity.TempVoteCount = TempVoteCount = animeInfo.TempVoteCount;
            upd.Entity.URL = URL = animeInfo.URL;
            upd.Entity.VoteCount = VoteCount = animeInfo.VoteCount;
            return true;
        }

        public SVR_AnimeSeries CreateAnimeSeriesAndGroup(SVR_AnimeSeries existingSeries = null, int? existingGroupID = null)
        {
            // Create a new AnimeSeries record
            SVR_AnimeSeries series;

            using (var txn = Repo.AnimeSeries.BeginAdd())
            {
                txn.Entity.Populate(this);
                // Populate before making a group to ensure IDs and stats are set for group filters.
                series = txn.Commit((false, false, false, false));
            }

            using (var txn = Repo.AnimeSeries.BeginAddOrUpdate(() => series))
            {
                if (existingGroupID == null)
                {
                    SVR_AnimeGroup grp = new AnimeGroupCreator().GetOrCreateSingleGroupForSeries(txn.Entity);
                    txn.Entity.AnimeGroupID = grp.AnimeGroupID;
                }
                else
                {
                    SVR_AnimeGroup grp = Repo.AnimeGroup.GetByID(existingGroupID.Value) ??
                                         new AnimeGroupCreator().GetOrCreateSingleGroupForSeries(txn.Entity);
                    txn.Entity.AnimeGroupID = grp.AnimeGroupID;
                }

                series = txn.Commit((false, false, false, false));
            }

            // check for TvDB associations
            if (Restricted == 0)
            {
                CommandRequest_TvDBSearchAnime cmd = new CommandRequest_TvDBSearchAnime(AnimeID, forced: false);
                cmd.Save();

                // check for Trakt associations
                if (ServerSettings.Instance.TraktTv.Enabled && !string.IsNullOrEmpty(ServerSettings.Instance.TraktTv.AuthToken))
                {
                    CommandRequest_TraktSearchAnime cmd2 = new CommandRequest_TraktSearchAnime(AnimeID, forced: false);
                    cmd2.Save();
                }

                if (AnimeType == (int)Shoko.Models.Enums.AnimeType.Movie)
                {
                    CommandRequest_MovieDBSearchAnime cmd3 =
                        new CommandRequest_MovieDBSearchAnime(AnimeID, false);
                    cmd3.Save();
                }
            }

            return series;
        }

        public bool PopulateAndSaveFromHTTP(Raw_AniDB_Anime animeInfo, List<Raw_AniDB_Episode> eps,
            List<Raw_AniDB_Anime_Title> titles,
            List<Raw_AniDB_Category> cats, List<Raw_AniDB_Tag> tags, List<Raw_AniDB_Character> chars,
            List<Raw_AniDB_ResourceLink> resources,
            List<Raw_AniDB_RelatedAnime> rels, List<Raw_AniDB_SimilarAnime> sims,
            List<Raw_AniDB_Recommendation> recs, bool downloadRelations, int relDepth)
        {
            using (var upd = Repo.AniDB_Anime.BeginAddOrUpdate(() => this))
            {
                logger.Trace("------------------------------------------------");
                logger.Trace($"PopulateAndSaveFromHTTP: for {animeInfo.AnimeID} - {animeInfo.MainTitle} @ Depth: {relDepth}/{ServerSettings.Instance.AniDb.MaxRelationDepth}");
                logger.Trace("------------------------------------------------");

                Stopwatch taskTimer = new Stopwatch();
                Stopwatch totalTimer = Stopwatch.StartNew();

                if (!Populate(animeInfo, upd))
                {
                    logger.Error("AniDB_Anime was unable to populate as it received invalid info. " +
                                 "This is not an error on our end. It is AniDB's issue, " +
                                 "as they did not return either an ID or a title for the anime.");
                    totalTimer.Stop();
                    return false;
                }

                taskTimer.Start();

                CreateEpisodes(eps, upd);
                taskTimer.Stop();
                logger.Trace("CreateEpisodes in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateTitles(titles, upd);
                taskTimer.Stop();
                logger.Trace("CreateTitles in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateTags(tags, upd);
                taskTimer.Stop();
                logger.Trace("CreateTags in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateCharacters(chars);
                taskTimer.Stop();
                logger.Trace("CreateCharacters in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateResources(resources, upd);
                taskTimer.Stop();
                logger.Trace("CreateResources in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateRelations(rels, downloadRelations, relDepth);
                taskTimer.Stop();
                logger.Trace("CreateRelations in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateSimilarAnime(sims);
                taskTimer.Stop();
                logger.Trace("CreateSimilarAnime in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                CreateRecommendations(recs);
                taskTimer.Stop();
                logger.Trace("CreateRecommendations in : " + taskTimer.ElapsedMilliseconds);
                taskTimer.Restart();

                upd.Commit();
                totalTimer.Stop();
                logger.Trace("TOTAL TIME in : " + totalTimer.ElapsedMilliseconds);
                logger.Trace("------------------------------------------------");

                return true;
            }
        }

        /// <summary>
        /// we are depending on the HTTP api call to get most of the info
        /// we only use UDP to get mssing information
        /// </summary>
        /// <param name="animeInfo"></param>
        public void PopulateAndSaveFromUDP(Raw_AniDB_Anime animeInfo)
        {
            using (var upd = Repo.AniDB_Anime.BeginAddOrUpdate(() => this))
            {
                // raw fields
                upd.Entity.reviewIDListRAW = reviewIDListRAW = animeInfo.ReviewIDListRAW;
                upd.Commit();
            }

            CreateAnimeReviews();
        }

        public void CreateEpisodes(List<Raw_AniDB_Episode> eps, IAtomic<SVR_AniDB_Anime, object> txn)
        {
            if (eps == null) return;

            txn.Entity.EpisodeCountSpecial = EpisodeCountSpecial = 0;
            txn.Entity.EpisodeCountNormal = EpisodeCountNormal = 0;

            List<SVR_AnimeEpisode> animeEpsToDelete = new List<SVR_AnimeEpisode>();
            List<AniDB_Episode> aniDBEpsToDelete = new List<AniDB_Episode>();

            foreach (Raw_AniDB_Episode epraw in eps)
            {
                //
                // we need to do this check because some times AniDB will replace an existing episode with a new episode
                List<AniDB_Episode> existingEps = Repo.AniDB_Episode.GetByAnimeIDAndEpisodeTypeNumber(
                    epraw.AnimeID, (EpisodeType) epraw.EpisodeType, epraw.EpisodeNumber);

                // delete any old records
                foreach (AniDB_Episode epOld in existingEps)
                {
                    if (epOld.EpisodeID != epraw.EpisodeID)
                    {
                        // first delete any AnimeEpisode records that point to the new anidb episode
                        SVR_AnimeEpisode aniep = Repo.AnimeEpisode.GetByAniDBEpisodeID(epOld.EpisodeID);
                        if (aniep != null)
                            animeEpsToDelete.Add(aniep);
                        aniDBEpsToDelete.Add(epOld);
                    }
                }
            }
            Repo.AnimeEpisode.Delete(animeEpsToDelete);
            Repo.AniDB_Episode.Delete(aniDBEpsToDelete);


            List<AniDB_Episode> epsToSave = new List<AniDB_Episode>();
            foreach (Raw_AniDB_Episode epraw in eps)
            {
                using (var upd = Repo.AniDB_Episode.BeginAddOrUpdate(() => Repo.AniDB_Episode.GetByEpisodeID(epraw.EpisodeID)))
                {
                    upd.Entity.Populate_RA(epraw);

                    // since the HTTP api doesn't return a count of the number of specials, we will calculate it here
                    if (upd.Entity.GetEpisodeTypeEnum() == EpisodeType.Episode)
                        txn.Entity.EpisodeCountNormal = EpisodeCountNormal++;

                    if (upd.Entity.GetEpisodeTypeEnum() == EpisodeType.Special)
                        txn.Entity.EpisodeCountSpecial = EpisodeCountSpecial++;
                }
            }

            txn.Entity.EpisodeCount = EpisodeCount = EpisodeCountSpecial + EpisodeCountNormal;
        }


        private void CreateTitles(List<Raw_AniDB_Anime_Title> titles, IAtomic<SVR_AniDB_Anime, object> txn)
        {
            if (titles == null) return;

            txn.Entity.AllTitles = AllTitles = string.Empty;

            List<AniDB_Anime_Title> titlesToDelete = Repo.AniDB_Anime_Title.GetByAnimeID(AnimeID);
            List<AniDB_Anime_Title> titlesToSave = new List<AniDB_Anime_Title>();
            foreach (Raw_AniDB_Anime_Title rawtitle in titles)
            {
                AniDB_Anime_Title title = new AniDB_Anime_Title();
                if (!title.Populate(rawtitle)) continue;
                titlesToSave.Add(title);

                if (AllTitles.Length > 0) AllTitles += "|";
                txn.Entity.AllTitles = AllTitles += rawtitle.Title;
            }
            Repo.AniDB_Anime_Title.Delete(titlesToDelete);
            Repo.AniDB_Anime_Title.BeginAdd(titlesToSave).Commit();
        }

        private void CreateTags(List<Raw_AniDB_Tag> tags, IAtomic<SVR_AniDB_Anime, object> txn)
        {
            if (tags == null) return;

            txn.Entity.AllTags = AllTags = string.Empty;

            List<AniDB_Anime_Tag> xrefsToDelete = new List<AniDB_Anime_Tag>();

            // find all the current links, and then later remove the ones that are no longer relevant
            List<AniDB_Anime_Tag> currentTags = Repo.AniDB_Anime_Tag.GetByAnimeID(AnimeID);
            List<int> newTagIDs = new List<int>();

            foreach (Raw_AniDB_Tag rawtag in tags)
            {
                using (var upd = Repo.AniDB_Tag.BeginAddOrUpdate(() => Repo.AniDB_Tag.GetByID(rawtag.TagID)))
                {
                    if (upd.IsNew())
                    {
                        // There are situations in which an ID may have changed, this is usually due to it being moved
                        var existingTags = Repo.AniDB_Tag.GetByName(rawtag.TagName).ToList();
                        var xrefsToRemap = existingTags.Select(s => Repo.AniDB_Anime_Tag.GetByID(s.AniDB_TagID))
                            .ToList();
                        Repo.AniDB_Anime_Tag.BatchAction(xrefsToRemap, xrefsToRemap.Count, (xref, _) => xref.TagID = rawtag.TagID);

                        // Delete the obsolete tag(s)
                        Repo.AniDB_Tag.Delete(existingTags);

                        // While we're at it, clean up other unreferenced tags
                        Repo.AniDB_Tag.Delete(Repo.AniDB_Tag.GetAll()
                            .Where(a => !Repo.AniDB_Anime_Tag.GetByTagID(a.TagID).Any()).ToList());
                    }

                    if (!upd.Entity.Populate(rawtag)) continue;

                    newTagIDs.Add(upd.Entity.TagID);

                    using (var xr = Repo.AniDB_Anime_Tag.BeginAddOrUpdate(() => Repo.AniDB_Anime_Tag.GetByAnimeIDAndTagID(rawtag.AnimeID, rawtag.TagID)))
                    {
                        xr.Entity.Populate(rawtag);
                    }

                    if (AllTags.Length > 0) txn.Entity.AllTags = AllTags += "|";
                    txn.Entity.AllTags = AllTags += upd.Entity.TagName;
                    upd.Commit();
                }
            }

            foreach (AniDB_Anime_Tag curTag in currentTags)
            {
                if (!newTagIDs.Contains(curTag.TagID))
                    xrefsToDelete.Add(curTag);
            }
            Repo.AniDB_Anime_Tag.Delete(xrefsToDelete);
        }

        private void CreateCharacters(List<Raw_AniDB_Character> chars)
        {
            if (chars == null) return;

            // delete all the existing cross references just in case one has been removed
            List<AniDB_Anime_Character> animeChars =
                Repo.AniDB_Anime_Character.GetByAnimeID(AnimeID);

            try
            {
                Repo.AniDB_Anime_Character.Delete(animeChars);
            }
            catch (Exception ex)
            {
                logger.Error($"Unable to Remove Characters for {MainTitle}: {ex}");
            }


            List<AniDB_Character> chrsToSave = new List<AniDB_Character>();
            List<AniDB_Anime_Character> xrefsToSave = new List<AniDB_Anime_Character>();

            Dictionary<int, AniDB_Seiyuu> seiyuuToSave = new Dictionary<int, AniDB_Seiyuu>();
            List<AniDB_Character_Seiyuu> seiyuuXrefToSave = new List<AniDB_Character_Seiyuu>();

            // delete existing relationships to seiyuu's
            List<AniDB_Character_Seiyuu> charSeiyuusToDelete = new List<AniDB_Character_Seiyuu>();
            foreach (Raw_AniDB_Character rawchar in chars)
            {
                // delete existing relationships to seiyuu's
                List<AniDB_Character_Seiyuu> allCharSei =
                    Repo.AniDB_Character_Seiyuu.GetByCharID(rawchar.CharID);
                foreach (AniDB_Character_Seiyuu xref in allCharSei)
                    charSeiyuusToDelete.Add(xref);
            }
            try
            {
                Repo.AniDB_Character_Seiyuu.Delete(charSeiyuusToDelete);
            }
            catch (Exception ex)
            {
                logger.Error($"Unable to Remove Seiyuus for {MainTitle}: {ex}");
            }

            string charBasePath = ImageUtils.GetBaseAniDBCharacterImagesPath() + Path.DirectorySeparatorChar;
            string creatorBasePath = ImageUtils.GetBaseAniDBCreatorImagesPath() + Path.DirectorySeparatorChar;
            foreach (Raw_AniDB_Character rawchar in chars)
            {
                try
                {
                    AniDB_Character chr = Repo.AniDB_Character.GetByCharID(rawchar.CharID) ??
                                          new AniDB_Character();

                    if (!chr.PopulateFromHTTP(rawchar)) continue;
                    chrsToSave.Add(chr);

                    var character = Repo.AnimeCharacter.GetByAniDBID(chr.CharID);
                    if (character == null)
                    {
                        character = new AnimeCharacter
                        {
                            AniDBID = chr.CharID,
                            Name = chr.CharName,
                            AlternateName = rawchar.CharKanjiName,
                            Description = chr.CharDescription,
                            ImagePath = chr.GetPosterPath()?.Replace(charBasePath, "")
                        };
                        // we need an ID for xref
                        Repo.AnimeCharacter.BeginAdd(character).Commit();
                    }

                    // create cross ref's between anime and character, but don't actually download anything
                    AniDB_Anime_Character anime_char = new AniDB_Anime_Character();
                    anime_char.Populate(rawchar);
                    xrefsToSave.Add(anime_char);

                    foreach (Raw_AniDB_Seiyuu rawSeiyuu in rawchar.Seiyuus)
                    {
                        try
                        {
                            // save the link between character and seiyuu
                            AniDB_Character_Seiyuu acc = Repo.AniDB_Character_Seiyuu.GetByCharIDAndSeiyuuID(rawchar.CharID,
                                rawSeiyuu.SeiyuuID);
                            if (acc == null)
                            {
                                acc = new AniDB_Character_Seiyuu
                                {
                                    CharID = chr.CharID,
                                    SeiyuuID = rawSeiyuu.SeiyuuID
                                };
                                seiyuuXrefToSave.Add(acc);
                            }

                            // save the seiyuu
                            AniDB_Seiyuu seiyuu = Repo.AniDB_Seiyuu.GetByID(rawSeiyuu.SeiyuuID);
                            if (seiyuu == null) seiyuu = new AniDB_Seiyuu();
                            seiyuu.PicName = rawSeiyuu.PicName;
                            seiyuu.SeiyuuID = rawSeiyuu.SeiyuuID;
                            seiyuu.SeiyuuName = rawSeiyuu.SeiyuuName;
                            seiyuuToSave[seiyuu.SeiyuuID] = seiyuu;

                            var staff = Repo.AnimeStaff.GetByAniDBID(seiyuu.SeiyuuID);
                            if (staff == null)
                            {
                                staff = new AnimeStaff
                                {
                                    // Unfortunately, most of the info is not provided
                                    AniDBID = seiyuu.SeiyuuID,
                                    Name = rawSeiyuu.SeiyuuName,
                                    ImagePath = seiyuu.GetPosterPath()?.Replace(creatorBasePath, "")
                                };
                                // we need an ID for xref
                                Repo.AnimeStaff.BeginAdd(staff).Commit();
                            }

                            var xrefAnimeStaff = Repo.CrossRef_Anime_Staff.GetByParts(AnimeID, character.CharacterID,
                                staff.StaffID, StaffRoleType.Seiyuu);
                            if (xrefAnimeStaff == null)
                            {
                                xrefAnimeStaff = new CrossRef_Anime_Staff
                                {
                                    AniDB_AnimeID = AnimeID,
                                    Language = "Japanese",
                                    RoleType = (int)StaffRoleType.Seiyuu,
                                    Role = rawchar.CharType,
                                    RoleID = character.CharacterID,
                                    StaffID = staff.StaffID,
                                };
                                Repo.CrossRef_Anime_Staff.BeginAdd(xrefAnimeStaff).Commit();
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error($"Unable to Populate and Save Seiyuus for {MainTitle}: {e}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Unable to Populate and Save Characters for {MainTitle}: {ex}");
                }
            }
            try
            {
                Repo.AniDB_Character.BeginAdd(chrsToSave).Commit();
                Repo.AniDB_Anime_Character.BeginAdd(xrefsToSave).Commit();
                Repo.AniDB_Seiyuu.BeginAdd(seiyuuToSave.Values.ToList()).Commit();
                Repo.AniDB_Character_Seiyuu.BeginAdd(seiyuuXrefToSave).Commit();
            }
            catch (Exception ex)
            {
                logger.Error($"Unable to Save Characters and Seiyuus for {MainTitle}: {ex}");
            }
        }

        public void CreateResources(List<Raw_AniDB_ResourceLink> resources, IAtomic<SVR_AniDB_Anime, object> upd)
        {
            if (resources == null) return;
            List<CrossRef_AniDB_MAL> malLinks = new List<CrossRef_AniDB_MAL>();
            foreach (Raw_AniDB_ResourceLink resource in resources)
            {
                switch (resource.Type)
                {
                    case AniDB_ResourceLinkType.ANN:
                        {
                            upd.Entity.ANNID = this.ANNID = resource.ID;
                            break;
                        }
                    case AniDB_ResourceLinkType.ALLCinema:
                        {
                            upd.Entity.AllCinemaID = this.AllCinemaID = resource.ID;
                            break;
                        }
                    case AniDB_ResourceLinkType.AnimeNFO:
                        {
                            upd.Entity.AnimeNfo = this.AnimeNfo = resource.ID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Site_JP:
                        {
                            upd.Entity.Site_JP = this.Site_JP = resource.RawID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Site_EN:
                        {
                            upd.Entity.Site_EN = this.Site_EN = resource.RawID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Wiki_EN:
                        {
                            upd.Entity.Wikipedia_ID = this.Wikipedia_ID = resource.RawID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Wiki_JP:
                        {
                            upd.Entity.WikipediaJP_ID = this.WikipediaJP_ID = resource.RawID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Syoboi:
                        {
                            upd.Entity.SyoboiID = this.SyoboiID = resource.ID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Anison:
                        {
                            upd.Entity.AnisonID = this.AnisonID = resource.ID;
                            break;
                        }
                    case AniDB_ResourceLinkType.Crunchyroll:
                        {
                            upd.Entity.CrunchyrollID = this.CrunchyrollID = resource.RawID;
                            break;
                        }
                    case AniDB_ResourceLinkType.MAL:
                        {
                            int id = resource.ID;
                            if (id == 0) break;
                            if (Repo.CrossRef_AniDB_MAL.GetByMALID(id) != null) continue;
                            CrossRef_AniDB_MAL xref = new CrossRef_AniDB_MAL
                            {
                                AnimeID = AnimeID,
                                CrossRefSource = (int)CrossRefSource.AniDB,
                                MALID = id,
                                StartEpisodeNumber = 1,
                                StartEpisodeType = 1
                            };

                            malLinks.Add(xref);
                            break;
                        }
                }
            }
            Repo.CrossRef_AniDB_MAL.BeginAdd(malLinks).Commit();
        }

        private void CreateRelations(List<Raw_AniDB_RelatedAnime> rels, bool downloadRelations, int relDepth)
        {
            if (rels == null) return;

            List<CommandRequest_GetAnimeHTTP> cmdsToSave = new List<CommandRequest_GetAnimeHTTP>();

            foreach (Raw_AniDB_RelatedAnime rawrel in rels)
            {
                using (var upd = Repo.AniDB_Anime_Relation.BeginAddOrUpdate(() => Repo.AniDB_Anime_Relation.GetByAnimeIDAndRelationID(rawrel.AnimeID, rawrel.RelatedAnimeID)))
                {
                    if (!upd.Entity.Populate(rawrel)) continue;

                    if (downloadRelations && relDepth < ServerSettings.Instance.AniDb.MaxRelationDepth)
                    {
                        logger.Info("Adding command to download related anime for {0} ({1}), related anime ID = {2}",
                            MainTitle, AnimeID, upd.Entity.RelatedAnimeID);

                        // I have disable the downloading of relations here because of banning issues
                        // basically we will download immediate relations, but not relations of relations

                        //CommandRequest_GetAnimeHTTP cr_anime = new CommandRequest_GetAnimeHTTP(rawrel.RelatedAnimeID, false, downloadRelations);
                        CommandRequest_GetAnimeHTTP cr_anime = new CommandRequest_GetAnimeHTTP(upd.Entity.RelatedAnimeID,
                            false, false, relDepth + 1);
                        cmdsToSave.Add(cr_anime);
                    }
                }
            }

            // this is not part of the session/transaction because it does other operations in the save
            cmdsToSave.ForEach(s => s.Save());
        }

        private void CreateSimilarAnime(List<Raw_AniDB_SimilarAnime> sims)
        {
            if (sims == null) return;

            List<AniDB_Anime_Similar> recsToSave = new List<AniDB_Anime_Similar>();

            foreach (Raw_AniDB_SimilarAnime rawsim in sims)
            {
                using (var upd = Repo.AniDB_Anime_Similar.BeginAddOrUpdate(() => Repo.AniDB_Anime_Similar.GetByAnimeIDAndSimilarID(rawsim.AnimeID, rawsim.SimilarAnimeID)))
                {
                    upd.Entity.Populate(rawsim);
                    upd.Commit();
                }
            }
        }

        private void CreateRecommendations(List<Raw_AniDB_Recommendation> recs)
        {
            if (recs == null) return;

            //AniDB_RecommendationRepository repRecs = new AniDB_RecommendationRepository();

            List<AniDB_Recommendation> recsToSave = new List<AniDB_Recommendation>();
            foreach (Raw_AniDB_Recommendation rawRec in recs)
            {
                using (var upd = Repo.AniDB_Recommendation.BeginAddOrUpdate(() => Repo.AniDB_Recommendation.GetByAnimeIDAndUserID(rawRec.AnimeID, rawRec.UserID)))
                {
                    upd.Entity.Populate_RA(rawRec);
                    upd.Commit();
                }
            }
        }

        private void CreateAnimeReviews()
        {
            if (reviewIDListRAW != null)
            //Only create relations if the origin of the data if from Raw (WebService/AniDB)
            {
                if (reviewIDListRAW.Trim().Length == 0)
                    return;

                //Delete old if changed
                List<AniDB_Anime_Review> animeReviews = Repo.AniDB_Anime_Review.GetByAnimeID(AnimeID);
                foreach (AniDB_Anime_Review xref in animeReviews)
                    Repo.AniDB_Anime_Review.Delete(xref.AniDB_Anime_ReviewID);


                string[] revs = reviewIDListRAW.Split(',');
                foreach (string review in revs)
                {
                    if (review.Trim().Length > 0)
                    {
                        int.TryParse(review.Trim(), out int rev);
                        if (rev != 0)
                        {
                            AniDB_Anime_Review csr = new AniDB_Anime_Review
                            {
                                AnimeID = AnimeID,
                                ReviewID = rev
                            };
                            Repo.AniDB_Anime_Review.BeginAdd(csr).Commit();
                        }
                    }
                }
            }
        }

        #endregion

        #region Contracts

        private CL_AniDB_Anime GenerateContract(List<AniDB_Anime_Title> titles)
        {
            List<CL_AniDB_Character> characters = GetCharactersContract();

            var movDbFanart = GetMovieDBFanarts();
            var tvDbFanart = GetTvDBImageFanarts();
            var tvDbBanners = GetTvDBImageWideBanners();

            CL_AniDB_Anime cl = GenerateContract(titles, null, characters, movDbFanart, tvDbFanart, tvDbBanners);
            AniDB_Anime_DefaultImage defFanart = GetDefaultFanart();
            AniDB_Anime_DefaultImage defPoster = GetDefaultPoster();
            AniDB_Anime_DefaultImage defBanner = GetDefaultWideBanner();

            cl.DefaultImageFanart = defFanart?.ToClient();
            cl.DefaultImagePoster = defPoster?.ToClient();
            cl.DefaultImageWideBanner = defBanner?.ToClient();

            return cl;
        }

        private CL_AniDB_Anime GenerateContract(List<AniDB_Anime_Title> titles, DefaultAnimeImages defaultImages,
            List<CL_AniDB_Character> characters, IEnumerable<MovieDB_Fanart> movDbFanart,
            IEnumerable<TvDB_ImageFanart> tvDbFanart,
            IEnumerable<TvDB_ImageWideBanner> tvDbBanners)
        {
            CL_AniDB_Anime cl = this.ToClient();
            cl.FormattedTitle = GetFormattedTitle(titles);
            cl.Characters = characters;

            if (defaultImages != null)
            {
                cl.DefaultImageFanart = defaultImages.Fanart?.ToContract();
                cl.DefaultImagePoster = defaultImages.Poster?.ToContract();
                cl.DefaultImageWideBanner = defaultImages.WideBanner?.ToContract();
            }

            cl.Fanarts = new List<CL_AniDB_Anime_DefaultImage>();
            if (movDbFanart != null && movDbFanart.Any())
            {
                cl.Fanarts.AddRange(movDbFanart.Select(a => new CL_AniDB_Anime_DefaultImage
                {
                    ImageType = (int)ImageEntityType.MovieDB_FanArt,
                    MovieFanart = a,
                    AniDB_Anime_DefaultImageID = a.MovieDB_FanartID
                }));
            }

            if (tvDbFanart != null && tvDbFanart.Any())
            {
                cl.Fanarts.AddRange(tvDbFanart.Select(a => new CL_AniDB_Anime_DefaultImage
                {
                    ImageType = (int)ImageEntityType.TvDB_FanArt,
                    TVFanart = a,
                    AniDB_Anime_DefaultImageID = a.TvDB_ImageFanartID
                }));
            }

            cl.Banners = tvDbBanners?.Select(a => new CL_AniDB_Anime_DefaultImage
            {
                ImageType = (int)ImageEntityType.TvDB_Banner,
                TVWideBanner = a,
                AniDB_Anime_DefaultImageID = a.TvDB_ImageWideBannerID
            })
                             .ToList();

            if (cl.Fanarts?.Count == 0) cl.Fanarts = null;
            if (cl.Banners?.Count == 0) cl.Banners = null;

            return cl;
        }

        public List<CL_AniDB_Character> GetCharactersContract()
        {
            List<CL_AniDB_Character> chars = new List<CL_AniDB_Character>();

            try
            {
                List<AniDB_Anime_Character> animeChars = Repo.AniDB_Anime_Character.GetByAnimeID(AnimeID);
                if (animeChars == null || animeChars.Count == 0) return chars;

                foreach (AniDB_Anime_Character animeChar in animeChars)
                {
                    AniDB_Character chr = Repo.AniDB_Character.GetByCharID(animeChar.CharID);
                    if (chr != null)
                        chars.Add(chr.ToClient(animeChar.CharType));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return chars;
        }

        public static void UpdateContractDetailedBatch(IReadOnlyCollection<SVR_AniDB_Anime> animeColl)
        {
            if (animeColl == null)
                throw new ArgumentNullException(nameof(animeColl));

            int[] animeIds = animeColl.Select(a => a.AnimeID).ToArray();

            var titlesByAnime = Repo.AniDB_Anime_Title.GetByAnimeIDs(animeIds);
            var animeTagsByAnime = Repo.AniDB_Anime_Tag.GetByAnimeIDs(animeIds);
            var tagsByAnime = Repo.AniDB_Tag.GetByAnimeIDs(animeIds);
            var custTagsByAnime = Repo.CustomTag.GetByAnimeIDs(animeIds);
            var voteByAnime = Repo.AniDB_Vote.GetByAnimeIDs(animeIds);
            var audioLangByAnime = Repo.Adhoc.GetAudioLanguageStatsByAnime(animeIds);
            var subtitleLangByAnime = Repo.Adhoc.GetSubtitleLanguageStatsByAnime(animeIds);
            var vidQualByAnime = Repo.Adhoc.GetAllVideoQualityByAnime(animeIds);
            var epVidQualByAnime = Repo.Adhoc.GetEpisodeVideoQualityStatsByAnime(animeIds);
            var defImagesByAnime = Repo.AniDB_Anime.GetDefaultImagesByAnime(animeIds);
            var charsByAnime = Repo.AniDB_Character.GetCharacterAndSeiyuuByAnime(animeIds);
            var movDbFanartByAnime = Repo.MovieDB_Fanart.GetByAnimeIDs(animeIds);
            var tvDbBannersByAnime = Repo.TvDB_ImageWideBanner.GetByAnimeIDs(animeIds);
            var tvDbFanartByAnime = Repo.TvDB_ImageFanart.GetByAnimeIDs(animeIds);

            foreach (SVR_AniDB_Anime anime in animeColl)
            {
                var contract = new CL_AniDB_AnimeDetailed();
                var animeTitles = titlesByAnime[anime.AnimeID];

                defImagesByAnime.TryGetValue(anime.AnimeID, out DefaultAnimeImages defImages);

                var characterContracts = charsByAnime[anime.AnimeID].Select(ac => ac.ToClient()).ToList();
                var movieDbFanart = movDbFanartByAnime[anime.AnimeID];
                var tvDbBanners = tvDbBannersByAnime[anime.AnimeID];
                var tvDbFanart = tvDbFanartByAnime[anime.AnimeID];

                contract.AniDBAnime = anime.GenerateContract(animeTitles.ToList(), defImages, characterContracts,
                    movieDbFanart, tvDbFanart, tvDbBanners);

                // Anime titles
                contract.AnimeTitles = titlesByAnime[anime.AnimeID]
                    .Select(t => new CL_AnimeTitle
                    {
                        AnimeID = t.AnimeID,
                        Language = t.Language,
                        Title = t.Title,
                        TitleType = t.TitleType
                    })
                    .ToList();

                // Seasons
                if (anime.AirDate != null)
                {
                    int beginYear = anime.AirDate.Value.Year;
                    int endYear = anime.EndDate?.Year ?? DateTime.Today.Year;
                    for (int year = beginYear; year <= endYear; year++)
                    {
                        foreach (AnimeSeason season in Enum.GetValues(typeof(AnimeSeason)))
                            if (anime.IsInSeason(season, year)) contract.Stat_AllSeasons.Add($"{season} {year}");
                    }
                }

                // Anime tags
                var dictAnimeTags = animeTagsByAnime[anime.AnimeID]
                    .ToDictionary(t => t.TagID);

                contract.Tags = tagsByAnime[anime.AnimeID]
                    .Select(t =>
                    {
                        CL_AnimeTag ctag = new CL_AnimeTag
                        {
                            GlobalSpoiler = t.GlobalSpoiler,
                            LocalSpoiler = t.LocalSpoiler,
                            TagDescription = t.TagDescription,
                            TagID = t.TagID,
                            TagName = t.TagName,
                            Weight = dictAnimeTags.TryGetValue(t.TagID, out AniDB_Anime_Tag animeTag) ? animeTag.Weight : 0
                        };

                        return ctag;
                    })
                    .ToList();

                // Custom tags
                contract.CustomTags = custTagsByAnime[anime.AnimeID];

                // Vote

                if (voteByAnime.TryGetValue(anime.AnimeID, out AniDB_Vote vote))
                {
                    contract.UserVote = vote;
                }


                // Subtitle languages
                contract.Stat_AudioLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                if (audioLangByAnime.TryGetValue(anime.AnimeID, out LanguageStat langStat))
                {
                    contract.Stat_AudioLanguages.UnionWith(langStat.LanguageNames);
                }

                // Audio languages
                contract.Stat_SubtitleLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                if (subtitleLangByAnime.TryGetValue(anime.AnimeID, out langStat))
                {
                    contract.Stat_SubtitleLanguages.UnionWith(langStat.LanguageNames);
                }

                // Anime video quality

                contract.Stat_AllVideoQuality = vidQualByAnime.TryGetValue(anime.AnimeID, out HashSet<string> vidQual)
                    ? vidQual
                    : new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                // Episode video quality

                contract.Stat_AllVideoQuality_Episodes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                if (epVidQualByAnime.TryGetValue(anime.AnimeID, out AnimeVideoQualityStat vidQualStat) &&
                    vidQualStat.VideoQualityEpisodeCount.Count > 0)
                {
                    contract.Stat_AllVideoQuality_Episodes.UnionWith(vidQualStat.VideoQualityEpisodeCount
                        .Where(kvp => kvp.Value >= anime.EpisodeCountNormal)
                        .Select(kvp => kvp.Key));
                }

                anime.Contract = contract;
            }
        }

        public void UpdateContractDetailed()
        {
            List<AniDB_Anime_Title> animeTitles = Repo.AniDB_Anime_Title.GetByAnimeID(AnimeID);
            CL_AniDB_AnimeDetailed cl = new CL_AniDB_AnimeDetailed
            {
                AniDBAnime = GenerateContract(animeTitles),


                AnimeTitles = new List<CL_AnimeTitle>(),
                Tags = new List<CL_AnimeTag>(),
                CustomTags = new List<CustomTag>()
            };

            // get all the anime titles
            if (animeTitles != null)
            {
                foreach (AniDB_Anime_Title title in animeTitles)
                {
                    CL_AnimeTitle ctitle = new CL_AnimeTitle
                    {
                        AnimeID = title.AnimeID,
                        Language = title.Language,
                        Title = title.Title,
                        TitleType = title.TitleType
                    };
                    cl.AnimeTitles.Add(ctitle);
                }
            }

            if (AirDate != null)
            {
                int beginYear = AirDate.Value.Year;
                int endYear = EndDate?.Year ?? DateTime.Today.Year;
                for (int year = beginYear; year <= endYear; year++)
                {
                    foreach (AnimeSeason season in Enum.GetValues(typeof(AnimeSeason)))
                        if (this.IsInSeason(season, year)) cl.Stat_AllSeasons.Add($"{season} {year}");
                }
            }

            Dictionary<int, AniDB_Anime_Tag> dictAnimeTags = new Dictionary<int, AniDB_Anime_Tag>();
            foreach (AniDB_Anime_Tag animeTag in GetAnimeTags())
                dictAnimeTags[animeTag.TagID] = animeTag;

            foreach (AniDB_Tag tag in GetAniDBTags())
            {
                CL_AnimeTag ctag = new CL_AnimeTag
                {
                    GlobalSpoiler = tag.GlobalSpoiler,
                    LocalSpoiler = tag.LocalSpoiler,
                    //ctag.Spoiler = tag.Spoiler;
                    //ctag.TagCount = tag.TagCount;
                    TagDescription = tag.TagDescription,
                    TagID = tag.TagID,
                    TagName = tag.TagName
                };
                if (dictAnimeTags.ContainsKey(tag.TagID))
                    ctag.Weight = dictAnimeTags[tag.TagID].Weight;
                else
                    ctag.Weight = 0;

                cl.Tags.Add(ctag);
            }


            // Get all the custom tags
            foreach (CustomTag custag in GetCustomTagsForAnime())
                cl.CustomTags.Add(custag);

            if (UserVote != null)
                cl.UserVote = UserVote;

            HashSet<string> audioLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            HashSet<string> subtitleLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            //logger.Trace(" XXXX 06");

            // audio languages
            Dictionary<int, LanguageStat> dicAudio =
                Repo.Adhoc.GetAudioLanguageStatsByAnime(AnimeID);
            foreach (KeyValuePair<int, LanguageStat> kvp in dicAudio)
            {
                foreach (string lanName in kvp.Value.LanguageNames)
                {
                    if (!audioLanguages.Contains(lanName))
                        audioLanguages.Add(lanName);
                }
            }

            //logger.Trace(" XXXX 07");

            // subtitle languages
            var dicSubtitle =
                Repo.Adhoc.GetSubtitleLanguageStatsByAnime(AnimeID);
            foreach ((_, LanguageStat lang) in dicSubtitle)
            {
                foreach (string lanName in lang.LanguageNames)
                {
                    if (!subtitleLanguages.Contains(lanName))
                        subtitleLanguages.Add(lanName);
                }
            }

            //logger.Trace(" XXXX 08");

            cl.Stat_AudioLanguages = audioLanguages;

            //logger.Trace(" XXXX 09");

            cl.Stat_SubtitleLanguages = subtitleLanguages;

            //logger.Trace(" XXXX 10");
            cl.Stat_AllVideoQuality = Repo.Adhoc.GetAllVideoQualityForAnime(AnimeID);

            AnimeVideoQualityStat stat = Repo.Adhoc.GetEpisodeVideoQualityStatsForAnime(AnimeID);
            cl.Stat_AllVideoQuality_Episodes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            if (stat != null && stat.VideoQualityEpisodeCount.Count > 0)
            {
                foreach (KeyValuePair<string, int> kvp in stat.VideoQualityEpisodeCount)
                {
                    if (kvp.Value >= EpisodeCountNormal)
                    {
                        cl.Stat_AllVideoQuality_Episodes.Add(kvp.Key);
                    }
                }
            }

            //logger.Trace(" XXXX 11");

            Contract = cl;
        }
        public Azure_AnimeFull ToAzure()
        {
            Azure_AnimeFull contract = new Azure_AnimeFull
            {
                Detail = new Azure_AnimeDetail(),
                Characters = new List<Azure_AnimeCharacter>(),
                Comments = new List<Azure_AnimeComment>()
            };
            contract.Detail.AllTags = TagsString;
            contract.Detail.AllCategories = TagsString;
            contract.Detail.AnimeID = AnimeID;
            contract.Detail.AnimeName = MainTitle;
            contract.Detail.AnimeType = this.GetAnimeTypeDescription();
            contract.Detail.Description = Description;
            contract.Detail.EndDateLong = AniDB.GetAniDBDateAsSeconds(EndDate);
            contract.Detail.StartDateLong = AniDB.GetAniDBDateAsSeconds(AirDate);
            contract.Detail.EpisodeCountNormal = EpisodeCountNormal;
            contract.Detail.EpisodeCountSpecial = EpisodeCountSpecial;
            contract.Detail.FanartURL = GetDefaultFanartOnlineURL();
            contract.Detail.OverallRating = this.GetAniDBRating();
            contract.Detail.PosterURL = string.Format(Constants.URLS.AniDB_Images, Picname);
            contract.Detail.TotalVotes = this.GetAniDBTotalVotes();


            List<AniDB_Anime_Character> animeChars = Repo.AniDB_Anime_Character.GetByAnimeID(AnimeID);

            if (animeChars != null && animeChars.Count > 0)
            {
                // first get all the main characters
                foreach (
                    AniDB_Anime_Character animeChar in
                    animeChars.Where(
                        item =>
                            item.CharType.Equals("main character in", StringComparison.InvariantCultureIgnoreCase)))
                {
                    AniDB_Character chr = Repo.AniDB_Character.GetByCharID(animeChar.CharID);
                    if (chr != null)
                        contract.Characters.Add(chr.ToContractAzure(animeChar));
                }

                // now get the rest
                foreach (
                    AniDB_Anime_Character animeChar in
                    animeChars.Where(
                        item =>
                            !item.CharType.Equals("main character in", StringComparison.InvariantCultureIgnoreCase))
                )
                {
                    AniDB_Character chr = Repo.AniDB_Character.GetByCharID(animeChar.CharID);
                    if (chr != null)
                        contract.Characters.Add(chr.ToContractAzure(animeChar));
                }
            }


            foreach (AniDB_Recommendation rec in Repo.AniDB_Recommendation.GetByAnimeID(AnimeID))
            {
                Azure_AnimeComment comment = new Azure_AnimeComment
                {
                    UserID = rec.UserID,
                    UserName = string.Empty,

                    // Comment details
                    CommentText = rec.RecommendationText,
                    IsSpoiler = false,
                    CommentDateLong = 0,

                    ImageURL = string.Empty
                };
                AniDBRecommendationType recType = (AniDBRecommendationType)rec.RecommendationType;
                switch (recType)
                {
                    case AniDBRecommendationType.ForFans:
                        comment.CommentType = (int)WhatPeopleAreSayingType.AniDBForFans;
                        break;
                    case AniDBRecommendationType.MustSee:
                        comment.CommentType = (int)WhatPeopleAreSayingType.AniDBMustSee;
                        break;
                    case AniDBRecommendationType.Recommended:
                        comment.CommentType = (int)WhatPeopleAreSayingType.AniDBRecommendation;
                        break;
                }

                comment.Source = "AniDB";
                contract.Comments.Add(comment);
            }

            return contract;
        }

        #endregion

        public static void UpdateStatsByAnimeID(int id)
        {
            SVR_AniDB_Anime an = Repo.AniDB_Anime.GetByAnimeID(id);
            if (an != null)
                Repo.AniDB_Anime.Touch(() => an);

            SVR_AnimeSeries series = Repo.AnimeSeries.GetByAnimeID(id);
            if (series != null)
            {
                using (var txn = Repo.AnimeSeries.BeginAddOrUpdate(() => series))
                {
                    // Update more than just stats in case the xrefs have changed
                    txn.Entity.UpdateStats(true, true, true);
                    txn.Commit((true, false, false, true));
                }
            }
        }

        public DateTime GetDateTimeUpdated()
        {
            var update = Repo.AniDB_AnimeUpdate.GetByAnimeID(AnimeID);
            return update?.UpdatedAt ?? DateTime.MinValue;
        }
    }
}
