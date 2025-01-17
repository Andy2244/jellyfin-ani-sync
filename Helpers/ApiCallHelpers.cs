using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jellyfin_ani_sync.Api;
using jellyfin_ani_sync.Api.Anilist;
using jellyfin_ani_sync.Api.Kitsu;
using jellyfin_ani_sync.Models;
using jellyfin_ani_sync.Models.Kitsu;
using jellyfin_ani_sync.Models.Mal;

namespace jellyfin_ani_sync.Helpers {
    public class ApiCallHelpers {
        private MalApiCalls _malApiCalls;
        private AniListApiCalls _aniListApiCalls;
        private KitsuApiCalls _kitsuApiCalls;

        /// <summary>
        /// This class attempts to combine the different APIs into a single form.
        /// </summary>
        /// <param name="malApiCalls"></param>
        /// <param name="aniListApiCalls"></param>
        /// <param name="kitsuApiCalls"></param>
        public ApiCallHelpers(MalApiCalls malApiCalls = null,
            AniListApiCalls aniListApiCalls = null,
            KitsuApiCalls kitsuApiCalls = null) {
            _malApiCalls = malApiCalls;
            _aniListApiCalls = aniListApiCalls;
            _kitsuApiCalls = kitsuApiCalls;
        }

        public async Task<List<Anime>> SearchAnime(string query) {
            if (_malApiCalls != null) {
                return await _malApiCalls.SearchAnime(query, new[] { "id", "title", "alternative_titles", "num_episodes", "status" });
            }

            if (_aniListApiCalls != null) {
                List<AniListSearch.Media> animeList = await _aniListApiCalls.SearchAnime(query);
                List<Anime> convertedList = new List<Anime>();
                if (animeList != null) {
                    foreach (AniListSearch.Media media in animeList) {
                        var synonyms = new List<string> {
                            { media.Title.Romaji },
                            { media.Title.UserPreferred }
                        };
                        synonyms.AddRange(media.Synonyms);
                        var anime = new Anime {
                            Id = media.Id,
                            Title = media.Title.English,
                            AlternativeTitles = new AlternativeTitles {
                                En = media.Title.English,
                                Ja = media.Title.Native,
                                Synonyms = synonyms
                            },
                            NumEpisodes = media.Episodes ?? 0,
                        };

                        switch (media.Status) {
                            case AniListSearch.AiringStatus.FINISHED:
                                anime.Status = AiringStatus.finished_airing;
                                break;
                            case AniListSearch.AiringStatus.RELEASING:
                                anime.Status = AiringStatus.currently_airing;
                                break;
                            case AniListSearch.AiringStatus.NOT_YET_RELEASED:
                            case AniListSearch.AiringStatus.CANCELLED:
                            case AniListSearch.AiringStatus.HIATUS:
                                anime.Status = AiringStatus.not_yet_aired;
                                break;
                        }

                        convertedList.Add(anime);
                    }
                }

                return convertedList;
            }

            if (_kitsuApiCalls != null) {
                List<KitsuSearch.KitsuAnime> animeList = await _kitsuApiCalls.SearchAnime(query);
                List<Anime> convertedList = new List<Anime>();
                if (animeList != null) {
                    foreach (KitsuSearch.KitsuAnime kitsuAnime in animeList) {
                        convertedList.Add(ClassConversions.ConvertKitsuAnime(kitsuAnime));
                    }
                }

                return convertedList;
            }

            return null;
        }

        public async Task<Anime> GetAnime(int id) {
            if (_malApiCalls != null) {
                return await _malApiCalls.GetAnime(id, new[] { "title", "related_anime", "my_list_status", "num_episodes" });
            }

            if (_aniListApiCalls != null) {
                AniListSearch.Media anime = await _aniListApiCalls.GetAnime(id);
                if (anime == null) return null;
                Anime convertedAnime = ClassConversions.ConvertAniListAnime(anime);

                if (anime.MediaListEntry != null) {
                    convertedAnime.MyListStatus.RewatchCount = anime.MediaListEntry.RepeatCount;

                    switch (anime.MediaListEntry.MediaListStatus) {
                        case AniListSearch.MediaListStatus.Current:
                            convertedAnime.MyListStatus.Status = Status.Plan_to_watch;
                            break;
                        case AniListSearch.MediaListStatus.Completed:
                        case AniListSearch.MediaListStatus.Repeating:
                            convertedAnime.MyListStatus.Status = Status.Completed;
                            break;
                        case AniListSearch.MediaListStatus.Dropped:
                            convertedAnime.MyListStatus.Status = Status.Dropped;
                            break;
                        case AniListSearch.MediaListStatus.Paused:
                            convertedAnime.MyListStatus.Status = Status.On_hold;
                            break;
                        case AniListSearch.MediaListStatus.Planning:
                            convertedAnime.MyListStatus.Status = Status.Plan_to_watch;
                            break;
                    }
                }

                convertedAnime.RelatedAnime = new List<RelatedAnime>();
                foreach (AniListSearch.MediaEdge relation in anime.Relations.Media) {
                    RelatedAnime relatedAnime = new RelatedAnime {
                        Anime = ClassConversions.ConvertAniListAnime(relation.Media)
                    };

                    switch (relation.RelationType) {
                        case AniListSearch.MediaRelation.Sequel:
                            relatedAnime.RelationType = RelationType.Sequel;
                            break;
                        case AniListSearch.MediaRelation.Side_Story:
                            relatedAnime.RelationType = RelationType.Side_Story;
                            break;
                        case AniListSearch.MediaRelation.Alternative:
                            relatedAnime.RelationType = RelationType.Alternative_Setting;
                            break;
                    }

                    convertedAnime.RelatedAnime.Add(relatedAnime);
                }

                return convertedAnime;
            }

            if (_kitsuApiCalls != null) {
                KitsuGet.KitsuGetAnime anime = await _kitsuApiCalls.GetAnime(id);
                if (anime == null) return null;
                Anime convertedAnime = ClassConversions.ConvertKitsuAnime(anime.KitsuAnimeData);

                int? userId = await _kitsuApiCalls.GetUserId();
                if (userId != null) {
                    KitsuUpdate.KitsuLibraryEntry userAnimeStatus = await _kitsuApiCalls.GetUserAnimeStatus(userId.Value, id);
                    if (userAnimeStatus is { Attributes: { } })
                        convertedAnime.MyListStatus = new MyListStatus {
                            NumEpisodesWatched = userAnimeStatus.Attributes.Progress ?? 0,
                            IsRewatching = userAnimeStatus.Attributes.Reconsuming ?? false,
                            RewatchCount = userAnimeStatus.Attributes.ReconsumeCount ?? 0
                        };

                    if (userAnimeStatus is { Attributes: { } })
                        switch (userAnimeStatus.Attributes.Status) {
                            case KitsuUpdate.Status.completed:
                                convertedAnime.MyListStatus.Status = Status.Completed;
                                break;
                            case KitsuUpdate.Status.current:
                                convertedAnime.MyListStatus.Status = Status.Watching;
                                break;
                            case KitsuUpdate.Status.dropped:
                                convertedAnime.MyListStatus.Status = Status.Dropped;
                                break;
                            case KitsuUpdate.Status.on_hold:
                                convertedAnime.MyListStatus.Status = Status.On_hold;
                                break;
                            case KitsuUpdate.Status.planned:
                                convertedAnime.MyListStatus.Status = Status.Plan_to_watch;
                                break;
                        }

                    convertedAnime.RelatedAnime = new List<RelatedAnime>();
                    foreach (KitsuSearch.KitsuAnime relatedAnime in anime.KitsuAnimeData.RelatedAnime) {
                        RelatedAnime convertedRelatedAnime = new RelatedAnime {
                            Anime = ClassConversions.ConvertKitsuAnime(relatedAnime)
                        };

                        switch (relatedAnime.RelationType) {
                            case KitsuMediaRelationship.RelationType.sequel:
                                convertedRelatedAnime.RelationType = RelationType.Sequel;
                                break;
                            case KitsuMediaRelationship.RelationType.side_story:
                            case KitsuMediaRelationship.RelationType.full_story:
                            case KitsuMediaRelationship.RelationType.parent_story:
                                convertedRelatedAnime.RelationType = RelationType.Side_Story;
                                break;
                            case KitsuMediaRelationship.RelationType.alternative_setting:
                            case KitsuMediaRelationship.RelationType.alternative_version:
                                convertedRelatedAnime.RelationType = RelationType.Alternative_Setting;
                                break;
                            case KitsuMediaRelationship.RelationType.spinoff:
                            case KitsuMediaRelationship.RelationType.adaptation:
                                convertedRelatedAnime.RelationType = RelationType.Spin_Off;
                                break;
                            default:
                                convertedRelatedAnime.RelationType = RelationType.Other;
                                break;
                        }

                        convertedAnime.RelatedAnime.Add(convertedRelatedAnime);
                    }
                }

                return convertedAnime;
            }

            return null;
        }

        public async Task<UpdateAnimeStatusResponse> UpdateAnime(int animeId, int numberOfWatchedEpisodes, Status status,
            bool? isRewatching = null, int? numberOfTimesRewatched = null, DateTime? startDate = null, DateTime? endDate = null) {
            if (_malApiCalls != null) {
                return await _malApiCalls.UpdateAnimeStatus(animeId, numberOfWatchedEpisodes, status, isRewatching, numberOfTimesRewatched, startDate, endDate);
            }

            if (_aniListApiCalls != null) {
                AniListSearch.MediaListStatus anilistStatus;
                switch (status) {
                    case Status.Watching:
                        anilistStatus = AniListSearch.MediaListStatus.Current;
                        break;
                    case Status.Completed:
                        anilistStatus = isRewatching != null && isRewatching.Value ? AniListSearch.MediaListStatus.Repeating : AniListSearch.MediaListStatus.Completed;
                        break;
                    case Status.On_hold:
                        anilistStatus = AniListSearch.MediaListStatus.Paused;
                        break;
                    case Status.Dropped:
                        anilistStatus = AniListSearch.MediaListStatus.Dropped;
                        break;
                    case Status.Plan_to_watch:
                        anilistStatus = AniListSearch.MediaListStatus.Planning;
                        break;
                    default:
                        anilistStatus = AniListSearch.MediaListStatus.Current;
                        break;
                }

                if (await _aniListApiCalls.UpdateAnime(animeId, anilistStatus, numberOfWatchedEpisodes, numberOfTimesRewatched, startDate, endDate)) {
                    return new UpdateAnimeStatusResponse();
                }
            }

            if (_kitsuApiCalls != null) {
                KitsuUpdate.Status kitsuStatus;

                switch (status) {
                    case Status.Watching:
                        kitsuStatus = KitsuUpdate.Status.current;
                        break;
                    case Status.Completed:
                    case Status.Rewatching:
                        kitsuStatus = isRewatching != null && isRewatching.Value ? KitsuUpdate.Status.current : KitsuUpdate.Status.completed;
                        break;
                    case Status.On_hold:
                        kitsuStatus = KitsuUpdate.Status.on_hold;
                        break;
                    case Status.Dropped:
                        kitsuStatus = KitsuUpdate.Status.dropped;
                        break;
                    case Status.Plan_to_watch:
                        kitsuStatus = KitsuUpdate.Status.planned;
                        break;
                    default:
                        kitsuStatus = KitsuUpdate.Status.current;
                        break;
                }

                if (await _kitsuApiCalls.UpdateAnimeStatus(animeId, numberOfWatchedEpisodes, kitsuStatus, isRewatching, numberOfTimesRewatched, startDate, endDate)) {
                    return new UpdateAnimeStatusResponse();
                }
            }

            return null;
        }

        public async Task<MalApiCalls.User> GetUser() {
            if (_malApiCalls != null) {
                return await _malApiCalls.GetUserInformation();
            }
            
            if (_aniListApiCalls != null) {
                AniListViewer.Viewer user = await _aniListApiCalls.GetCurrentUser();
                return ClassConversions.ConvertUser(user.Id, user.Name);
            }

            if (_kitsuApiCalls != null) {
                var user = await _kitsuApiCalls.GetUserId();
                if (user != null)
                    return new MalApiCalls.User {
                        Id = user.Value
                    };
            }

            return null;
        }

        public async Task<List<Anime>> GetAnimeList(Status status, int? userId = null) {
            if (_malApiCalls != null) {
                var malAnimeList = await _malApiCalls.GetUserAnimeList(Status.Completed);
                return malAnimeList?.Select(animeList => animeList.Anime).ToList();
            }

            if (_aniListApiCalls != null && userId != null) {
                AniListSearch.MediaListStatus anilistStatus;
                switch (status) {
                    case Status.Watching:
                        anilistStatus = AniListSearch.MediaListStatus.Current;
                        break;
                    case Status.Completed:
                        anilistStatus = AniListSearch.MediaListStatus.Completed;
                        break;
                    case Status.On_hold:
                        anilistStatus = AniListSearch.MediaListStatus.Paused;
                        break;
                    case Status.Dropped:
                        anilistStatus = AniListSearch.MediaListStatus.Dropped;
                        break;
                    case Status.Plan_to_watch:
                        anilistStatus = AniListSearch.MediaListStatus.Planning;
                        break;
                    default:
                        anilistStatus = AniListSearch.MediaListStatus.Current;
                        break;
                }

                var animeList = await _aniListApiCalls.GetAnimeList(userId.Value, anilistStatus);
                List<Anime> convertedList = new List<Anime>();
                if (animeList != null) {
                    foreach (var media in animeList) {
                        int lastIndex = media.Media.SiteUrl.LastIndexOf("/", StringComparison.CurrentCulture);
                        if (lastIndex != -1) {
                            DateTime finishDate = new DateTime();
                            if (media.CompletedAt is { Year: { }, Month: { }, Day: { } }) {
                                finishDate = new DateTime(media.CompletedAt.Year.Value, media.CompletedAt.Month.Value, media.CompletedAt.Day.Value);
                            }

                            convertedList.Add(new Anime {
                                Id = int.TryParse(media.Media.SiteUrl.Substring(lastIndex + 1, media.Media.SiteUrl.Length - lastIndex - 1), out int id) ? id : 0,
                                MyListStatus = new MyListStatus {
                                    FinishDate = finishDate.ToShortDateString(),
                                    NumEpisodesWatched = media.Progress ?? -1
                                }
                            });
                        }
                    }
                }

                return convertedList;
            }

            if (_kitsuApiCalls != null && userId != null) {
                KitsuUpdate.KitsuLibraryEntryListRoot animeList = await _kitsuApiCalls.GetUserAnimeList(userId.Value, status: KitsuUpdate.Status.completed);
                if (animeList != null) {
                    List<Anime> convertedList = new List<Anime>();
                    foreach (KitsuUpdate.KitsuLibraryEntry kitsuLibraryEntry in animeList.Data) {
                        Anime toAddAnime = new Anime();
                        if (kitsuLibraryEntry.Relationships != null &&
                            kitsuLibraryEntry.Relationships.AnimeData != null &&
                            kitsuLibraryEntry.Relationships.AnimeData.Anime != null) {
                            toAddAnime.Id = kitsuLibraryEntry.Relationships.AnimeData.Anime.Id;
                        }

                        toAddAnime.MyListStatus = new MyListStatus();
                        if (kitsuLibraryEntry.Attributes != null) {
                            toAddAnime.MyListStatus.FinishDate = kitsuLibraryEntry.Attributes.FinishedAt.ToString();
                            if (kitsuLibraryEntry.Attributes.Progress != null) {
                                toAddAnime.MyListStatus.NumEpisodesWatched = kitsuLibraryEntry.Attributes.Progress.Value;
                            }
                        }
                        
                        convertedList.Add(toAddAnime);
                    }

                    return convertedList;
                }
            }

            return null;
        }
    }
}