﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Force.DeepCloner;
using Shoko.Commons.Extensions;
using Shoko.Server.Databases;
using Shoko.Server.Repositories.ReaderWriterLockExtensions;

namespace Shoko.Server.Repositories
{
    public class AtomicLockBatchUpdate<T, TS, TT> : IBatchAtomic<T, TT>, IEnumerable<T> where T : class, new()
    {
        private readonly BaseRepository<T, TS, TT> _repo;
        private readonly bool _delete_not_updated;
        private IDisposable _lock;
        private readonly Dictionary<T, T> _originalItems;

        internal AtomicLockBatchUpdate(BaseRepository<T, TS, TT> repo, Func<List<T>> find_original_items = null, bool delete_not_updated = false)
        {
            _lock = _repo.RepoLock.WriterLock();
            _repo = repo;
            _originalItems = find_original_items != null ? find_original_items().ToDictionary(a => a.DeepClone(), a => a) : new Dictionary<T, T>();
            _delete_not_updated = delete_not_updated;
        }

        public T FindOrCreate(Func<T, bool> predicate)
        {
            T value = _originalItems.Keys.FirstOrDefault(predicate) ?? Create();
            return value;
        }
        public T Find(Func<T, bool> predicate)
        {
            return _originalItems.Keys.FirstOrDefault(predicate);
        }



        private Dictionary<T, T> _references { get; } = new Dictionary<T, T>();

        public void Update(T a)
        {
            if (a == null)
                return;
            if (_originalItems.ContainsKey(a))
                _references[a] = _originalItems[a];
        }

        public T Create()
        {
            T a=new T();
            _repo.Context.SetLocalKey(a, _repo.GetNextAutoGen);
            _references[a] = null;
            return a;
        }

        public List<T> Commit(TT pars = default(TT))
        {
            Dictionary<T, object> savedobjects = new Dictionary<T, object>();
            if (_delete_not_updated)
            {
                foreach (T t in _originalItems.Keys.ToList())
                {
                    if (_references.ContainsKey(t))
                        _originalItems.Remove(t);
                }

                if (_originalItems.Keys.Count > 0)
                {
                    foreach (T e in _originalItems.Values)
                        savedobjects[e] = _repo.BeginDelete(e, pars);
                    _repo.Table.RemoveRange(_originalItems.Values);
                    _repo.Context.SaveChanges();
                    if (_repo.IsCached)
                        _originalItems.Values.ForEach(_repo.Cache.Remove);
                    foreach (T e in _originalItems.Values)
                        _repo.EndDelete(e, savedobjects[e], pars);
                }
            }

            List<T> returns = new List<T>();
            if (_references.Count > 0)
            {
                Dictionary<T, object> savedObjects = new Dictionary<T, object>();
                foreach (T t in _references.Keys)
                {
                    savedObjects[t] = _repo.BeginSave(t, _references[t], pars);
                }

                var updates = _references.Where(a => a.Value != null).ToList();
                var creates = _references.Where(a => a.Value == null).ToList();
                using (_repo.RepoLock.WriterLock())
                {
                    foreach (KeyValuePair<T, T> r in updates)
                    {
                        r.Key.DeepCloneTo(r.Value); //Tried to be 100% atomic and failed miserably, so is 99%. 
                        //If we replace Original with Entity in cache (updating with 'this' as the model to update, will not get the changes).
                        //So this is the best effort
                        returns.Add(r.Value);
                    }

                    foreach (KeyValuePair<T, T> r in creates)
                    {
                        _repo.Table.Add(r.Key);
                        returns.Add(r.Key);
                    }

                    if (_repo.IsCached)
                        returns.ForEach(_repo.Cache.Update);
                }

                _repo.Context.SaveChanges();
                foreach (T t in returns)
                {
                    _repo.EndSave(t, savedObjects[t], pars);
                }
            }

            return returns;
        }

        public void Dispose()
        {
            Release();
        }

        private void Release()
        {
            if (_lock != null)
            {
                _lock.Dispose();
                _lock = null;
            }

            _references.Clear();
            _originalItems.Clear();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _originalItems.Keys.GetEnumerator();

        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _originalItems.Keys.GetEnumerator();
        }
    }
}