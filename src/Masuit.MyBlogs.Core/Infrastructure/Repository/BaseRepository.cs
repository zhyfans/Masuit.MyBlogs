﻿using Collections.Pooled;
using Dispose.Scope;
using EFCoreSecondLevelCacheInterceptor;
using Masuit.LuceneEFCore.SearchEngine;
using Masuit.MyBlogs.Core.Infrastructure.Repository.Interface;
using Masuit.Tools.Core.AspNetCore;

namespace Masuit.MyBlogs.Core.Infrastructure.Repository;

/// <summary>
/// DAL基类
/// </summary>
/// <typeparam name="T">实体类型</typeparam>
public abstract class BaseRepository<T> : Disposable, IBaseRepository<T> where T : LuceneIndexableBaseEntity
{
    public virtual DataContext DataContext { get; set; }

    /// <summary>
    /// 获取所有实体
    /// </summary>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IQueryable<T> GetAll()
    {
        return DataContext.Set<T>();
    }

    /// <summary>
    /// 获取所有实体（不跟踪）
    /// </summary>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IQueryable<T> GetAllNoTracking()
    {
        return DataContext.Set<T>().AsNoTracking();
    }

    /// <summary>
    /// 获取所有实体
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IOrderedQueryable<T> GetAll<TS>(Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? DataContext.Set<T>().OrderBy(orderby) : DataContext.Set<T>().OrderByDescending(orderby);
    }

    /// <summary>
    /// 获取所有实体（不跟踪）
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IOrderedQueryable<T> GetAllNoTracking<TS>(Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? DataContext.Set<T>().AsNoTracking().OrderBy(orderby) : DataContext.Set<T>().AsNoTracking().OrderByDescending(orderby);
    }

    /// <summary>
    /// 从二级缓存获取所有实体
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns></returns>
    public virtual PooledList<T> GetAllFromCache<TS>(Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return GetAllNoTracking(orderby, isAsc).Cacheable().ToPooledListScope();
    }

    /// <summary>
    /// 基本查询方法，获取一个集合
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IQueryable<T> GetQuery(Expression<Func<T, bool>> where)
    {
        return DataContext.Set<T>().Where(where);
    }

    /// <summary>
    /// 基本查询方法，获取一个集合
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="where">查询条件</param>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IOrderedQueryable<T> GetQuery<TS>(Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? DataContext.Set<T>().Where(where).OrderBy(orderby) : DataContext.Set<T>().Where(where).OrderByDescending(orderby);
    }

    /// <summary>
    /// 基本查询方法，获取一个集合，优先从二级缓存读取
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns></returns>
    public virtual PooledList<T> GetQueryFromCache(Expression<Func<T, bool>> where)
    {
        return DataContext.Set<T>().Where(where).AsNoTracking().Cacheable().ToPooledListScope();
    }

    /// <summary>
    /// 基本查询方法，获取一个集合，优先从二级缓存读取
    /// </summary>
    /// <typeparam name="TS">排序字段</typeparam>
    /// <param name="where">查询条件</param>
    /// <param name="orderby">排序方式</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns></returns>
    public virtual PooledList<T> GetQueryFromCache<TS>(Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return GetQueryNoTracking(where, orderby, isAsc).Cacheable().ToPooledListScope();
    }

    /// <summary>
    /// 基本查询方法，获取一个集合（不跟踪实体）
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IQueryable<T> GetQueryNoTracking(Expression<Func<T, bool>> where)
    {
        return DataContext.Set<T>().Where(where).AsNoTracking();
    }

    /// <summary>
    /// 基本查询方法，获取一个集合（不跟踪实体）
    /// </summary>
    /// <typeparam name="TS">排序字段</typeparam>
    /// <param name="where">查询条件</param>
    /// <param name="orderby">排序方式</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>还未执行的SQL语句</returns>
    public virtual IOrderedQueryable<T> GetQueryNoTracking<TS>(Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? DataContext.Set<T>().Where(where).AsNoTracking().OrderBy(orderby) : DataContext.Set<T>().Where(where).AsNoTracking().OrderByDescending(orderby);
    }

    /// <summary>
    /// 获取第一条数据
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>实体</returns>
    public virtual T Get(Expression<Func<T, bool>> where)
    {
        return EF.CompileQuery((DataContext ctx) => ctx.Set<T>().FirstOrDefault(where))(DataContext);
    }

    /// <summary>
    /// 获取第一条数据
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>实体</returns>
    public Task<T> GetFromCacheAsync(Expression<Func<T, bool>> @where)
    {
        return DataContext.Set<T>().Where(where).AsNoTracking().Cacheable().FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取第一条数据
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="where">查询条件</param>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>实体</returns>
    public virtual T Get<TS>(Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? EF.CompileQuery((DataContext ctx) => ctx.Set<T>().OrderBy(orderby).FirstOrDefault(where))(DataContext) : EF.CompileQuery((DataContext ctx) => ctx.Set<T>().OrderByDescending(orderby).FirstOrDefault(where))(DataContext);
    }

    /// <summary>
    /// 获取第一条数据
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>实体</returns>
    public virtual Task<T> GetAsync(Expression<Func<T, bool>> where)
    {
        return EF.CompileAsyncQuery((DataContext ctx) => ctx.Set<T>().FirstOrDefault(where))(DataContext);
    }

    /// <summary>
    /// 获取第一条数据
    /// </summary>
    /// <typeparam name="TS">排序</typeparam>
    /// <param name="where">查询条件</param>
    /// <param name="orderby">排序字段</param>
    /// <param name="isAsc">是否升序</param>
    /// <returns>实体</returns>
    public virtual Task<T> GetAsync<TS>(Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? EF.CompileAsyncQuery((DataContext ctx) => ctx.Set<T>().OrderBy(orderby).FirstOrDefault(where))(DataContext) : EF.CompileAsyncQuery((DataContext ctx) => ctx.Set<T>().OrderByDescending(orderby).FirstOrDefault(where))(DataContext);
    }

    /// <summary>
    /// 获取第一条数据（不跟踪实体）
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>实体</returns>
    public virtual T GetNoTracking(Expression<Func<T, bool>> where)
    {
        return EF.CompileQuery((DataContext ctx) => ctx.Set<T>().AsNoTracking().FirstOrDefault(where))(DataContext);
    }

    /// <summary>
    /// 根据ID找实体
    /// </summary>
    /// <param name="id">实体id</param>
    /// <returns>实体</returns>
    public virtual T GetById(int id)
    {
        return EF.CompileQuery((DataContext ctx, int xid) => ctx.Set<T>().FirstOrDefault(t => t.Id == xid))(DataContext, id);
    }

    /// <summary>
    /// 根据ID找实体(异步)
    /// </summary>
    /// <param name="id">实体id</param>
    /// <returns>实体</returns>
    public virtual Task<T> GetByIdAsync(int id)
    {
        return EF.CompileAsyncQuery((DataContext ctx, int xid) => ctx.Set<T>().FirstOrDefault(t => t.Id == xid))(DataContext, id);
    }

    /// <summary>
    /// 标准分页查询方法
    /// </summary>
    /// <typeparam name="TS"></typeparam>
    /// <param name="pageIndex">第几页</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="where">where Lambda条件表达式</param>
    /// <param name="orderby">orderby Lambda条件表达式</param>
    /// <param name="isAsc">升序降序</param>
    /// <returns></returns>
    public virtual PagedList<T> GetPages<TS>(int pageIndex, int pageSize, Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc)
    {
        return isAsc ? DataContext.Set<T>().Where(where).OrderBy(orderby).ToPagedList(pageIndex, pageSize) : DataContext.Set<T>().Where(where).OrderByDescending(orderby).ToPagedList(pageIndex, pageSize);
    }

    /// <summary>
    /// 标准分页查询方法
    /// </summary>
    /// <typeparam name="TS"></typeparam>
    /// <param name="pageIndex">第几页</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="where">where Lambda条件表达式</param>
    /// <param name="orderby">orderby Lambda条件表达式</param>
    /// <param name="isAsc">升序降序</param>
    /// <returns></returns>
    public virtual Task<PagedList<T>> GetPagesAsync<TS>(int pageIndex, int pageSize, Expression<Func<T, bool>> @where, Expression<Func<T, TS>> orderby, bool isAsc)
    {
        return isAsc ? DataContext.Set<T>().Where(where).OrderBy(orderby).ToPagedListAsync(pageIndex, pageSize) : DataContext.Set<T>().Where(where).OrderByDescending(orderby).ToPagedListAsync(pageIndex, pageSize);
    }

    /// <summary>
    /// 标准分页查询方法（不跟踪实体）
    /// </summary>
    /// <typeparam name="TS"></typeparam>
    /// <param name="pageIndex">第几页</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="where">where Lambda条件表达式</param>
    /// <param name="orderby">orderby Lambda条件表达式</param>
    /// <param name="isAsc">升序降序</param>
    /// <returns></returns>
    public virtual PagedList<T> GetPagesNoTracking<TS>(int pageIndex, int pageSize, Expression<Func<T, bool>> where, Expression<Func<T, TS>> orderby, bool isAsc = true)
    {
        return isAsc ? DataContext.Set<T>().Where(where).AsNoTracking().OrderBy(orderby).ToPagedList(pageIndex, pageSize) : DataContext.Set<T>().Where(where).AsNoTracking().OrderByDescending(orderby).ToPagedList(pageIndex, pageSize);
    }

    /// <summary>
    /// 根据ID删除实体
    /// </summary>
    /// <param name="id">实体id</param>
    /// <returns>删除成功</returns>
    public virtual bool DeleteById(int id)
    {
        return DataContext.Set<T>().Where(t => t.Id == id).ExecuteDelete() > 0;
    }

    /// <summary>
    /// 根据ID删除实体
    /// </summary>
    /// <param name="id">实体id</param>
    /// <returns>删除成功</returns>
    public virtual Task<int> DeleteByIdAsync(int id)
    {
        return DataContext.Set<T>().Where(t => t.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>
    /// 删除实体
    /// </summary>
    /// <param name="t">需要删除的实体</param>
    /// <returns>删除成功</returns>
    public virtual bool DeleteEntity(T t)
    {
        DataContext.Entry(t).State = EntityState.Unchanged;
        DataContext.Entry(t).State = EntityState.Deleted;
        DataContext.Remove(t);
        return true;
    }

    /// <summary>
    /// 根据条件删除实体
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>删除成功</returns>
    public virtual int DeleteEntity(Expression<Func<T, bool>> where)
    {
        return DataContext.Set<T>().Where(where).ExecuteDelete();
    }

    /// <summary>
    /// 添加实体
    /// </summary>
    /// <param name="t">需要添加的实体</param>
    /// <returns>添加成功</returns>
    public T AddEntity(T t)
    {
        DataContext.Add(t);
        return t;
    }

    /// <summary>
    /// 添加或更新实体
    /// </summary>
    /// <param name="key">更新键规则</param>
    /// <param name="t">需要保存的实体</param>
    /// <returns>保存成功</returns>
    public T AddOrUpdate<TKey>(Expression<Func<T, TKey>> key, T t)
    {
        DataContext.Set<T>().AddOrUpdate(key, t);
        return t;
    }

    /// <summary>
    /// 添加或更新实体
    /// </summary>
    /// <param name="key">更新键规则</param>
    /// <param name="entities">需要保存的实体</param>
    /// <returns>保存成功</returns>
    public void AddOrUpdate<TKey>(Expression<Func<T, TKey>> key, IEnumerable<T> entities)
    {
        DataContext.Set<T>().AddOrUpdate(key, entities);
    }

    /// <summary>
    /// 统一保存数据
    /// </summary>
    /// <returns>受影响的行数</returns>
    public virtual int SaveChanges()
    {
        return DataContext.SaveChanges();
    }

    /// <summary>
    /// 统一保存数据（异步）
    /// </summary>
    /// <returns>受影响的行数</returns>
    public virtual Task<int> SaveChangesAsync()
    {
        return DataContext.SaveChangesAsync();
    }

    /// <summary>
    /// 判断实体是否在数据库中存在
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>是否存在</returns>
    public virtual bool Any(Expression<Func<T, bool>> where)
    {
        return EF.CompileQuery((DataContext ctx) => ctx.Set<T>().Any(where))(DataContext);
    }

    /// <summary>
    /// 符合条件的个数
    /// </summary>
    /// <param name="where">查询条件</param>
    /// <returns>是否存在</returns>
    public virtual int Count(Expression<Func<T, bool>> where)
    {
        return EF.CompileQuery((DataContext ctx) => ctx.Set<T>().Count(where))(DataContext);
    }

    /// <summary>
    /// 删除多个实体
    /// </summary>
    /// <param name="list">实体集合</param>
    /// <returns>删除成功</returns>
    public virtual bool DeleteEntities(IEnumerable<T> list)
    {
        DataContext.RemoveRange(list);
        return true;
    }

    public override void Dispose(bool disposing)
    {
    }

    public T this[int id] => GetById(id);
}