﻿using Autofac;
using Autofac.Extras.DynamicProxy;
using Autofac.Integration.Mvc;
using Castle.DynamicProxy;
using Lib.data;
using Lib.events;
using Lib.extension;
using Lib.helper;
using Lib.infrastructure;
using Lib.mvc.plugin;
using Lib.task;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Lib.ioc
{
    /// <summary>
    /// 注册IOC依赖
    /// 搜索“autofac泛型注入”
    /// </summary>
    public abstract class DependencyRegistrarBase : IDependencyRegistrar
    {
        [Obsolete("计划过期，使用" + nameof(InterceptInstanceAttribute) + "指定需要拦截的类")]
        public abstract bool Intercept { get; }

        public abstract void Register(ref ContainerBuilder builder);

        private readonly IDictionary<Assembly, List<Type>> _cache = new Dictionary<Assembly, List<Type>>();
        //private readonly IDictionary<Type, (bool intercept, bool single_instance)> _cache_p = new Dictionary<Type, (bool intercept, bool single_instance)>();

        private List<Type> CachedClass(Assembly a)
        {
            if (!this._cache.ContainsKey(a))
            {
                this._cache[a] = a.FindAllRegistableClass().ToList();
            }

            return this._cache[a] ?? throw new Exception("无法获取可以注册的类");
        }

        /// <summary>
        /// 自动注册
        /// </summary>
        protected virtual void AutoRegistered(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                var types = this.CachedClass(a).Where(x => x.IsAssignableTo_<IAutoRegistered>()).ToArray();
                foreach (var t in types)
                {
                    var reg = builder.RegisterType(t).AsSelf().AsImplementedInterfaces();
                    if (t.IsSingleInstance())
                    {
                        reg.SingleInstance();
                    }

                    if (t.IsInterceptClass())
                    {
                        reg = reg.EnableClassInterceptors();
                    }
                }
            }
        }

        /// <summary>
        /// 注册仓库
        /// </summary>
        [Obsolete("使用" + nameof(RegDataRepositoryProvider) + "和" + nameof(RegDataRepository_) + "替代")]
        protected virtual void RegDataRepository(ref ContainerBuilder builder, params Assembly[] ass)
        {
            //注册泛型
            this.RegDataRepositoryProvider(ref builder, typeof(EFRepository<>));
            this.RegDataRepository_(ref builder, ass);
        }

        /// <summary>
        /// 使用仓储实现
        /// </summary>
        protected virtual void RegDataRepositoryProvider(ref ContainerBuilder builder, Type t)
        {
            if (!t.IsGenericType) { throw new Exception($"{t.GetType()}不是泛型"); }
            builder.RegisterGeneric(t).AsSelf().As(typeof(IRepository<>));
        }

        /// <summary>
        /// 注册所有实现了仓储的类
        /// </summary>
        protected virtual void RegDataRepository_(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                foreach (var t in this.CachedClass(a))
                {
                    var all_interfaces = t.GetAllInterfaces_().ToArray();
                    if (t.BaseType != null && all_interfaces.Any(x => x.IsGenericType_(typeof(IRepository<>))))
                    {
                        var reg = builder.RegisterType(t).AsSelf().As(t.BaseType).As(all_interfaces);
                    }
                }
            }
        }

        /// <summary>
        /// 注册logic service
        /// </summary>
        protected virtual void RegServiceProvider(ref ContainerBuilder builder, Type t)
        {
            if (!t.IsGenericType) { throw new Exception($"{t.GetType()}不是泛型"); }
            builder.RegisterGeneric(t).AsSelf().As(typeof(IServiceBase<>));
        }

        /// <summary>
        /// 注册服务
        /// </summary>
        protected virtual void RegService_(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                foreach (var t in this.CachedClass(a))
                {
                    var all_interfaces = t.GetAllInterfaces_().ToArray();
                    if (t.BaseType != null && all_interfaces.Any(x => x.IsGenericType_(typeof(IServiceBase<>))))
                    {
                        var reg = builder.RegisterType(t).AsSelf().As(t.BaseType).AsImplementedInterfaces();
                        if (t.IsInterceptClass())
                        {
                            reg = reg.EnableClassInterceptors();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 注册服务
        /// </summary>
        [Obsolete("使用" + nameof(RegServiceProvider) + "和" + nameof(RegService_) + "替代")]
        protected virtual void RegService(ref ContainerBuilder builder, params Assembly[] ass)
        {
            //注册泛型
            this.RegServiceProvider(ref builder, typeof(ServiceBase<>));
            this.RegService_(ref builder, ass);
        }

        /// <summary>
        /// 注册aop拦截类
        /// </summary>
        protected virtual void RegAop(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                var tps = this.CachedClass(a).Where(x => x.IsAssignableTo_<IInterceptor>()).ToArray();
                builder.RegisterTypes(tps);
            }
        }

        /// <summary>
        /// 注册事件
        /// </summary>
        protected virtual void RegEvent(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                try
                {
                    foreach (var t in this.CachedClass(a))
                    {
                        var interfaces = t.GetInterfaces().Where(x => x.IsGenericType_(typeof(IConsumer<>))).ToArray();
                        if (interfaces?.Count() > 0)
                        {
                            //注册到所有接口
                            builder.RegisterType(t).AsSelf().As(interfaces);
                        }
                    }
                }
                catch (Exception e)
                {
                    //Entity Framework 6不允许get types，抛了一个异常
                    e.AddLog("注册事件发布异常");
                    continue;
                }
            }
            builder.RegisterType<EventPublisher>().AsSelf().As<IEventPublisher>().SingleInstance();
        }

        /// <summary>
        /// 获取插件程序集(还需完善)
        /// </summary>
        protected virtual List<Assembly> FindPluginAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Where(x => x.FullName.StartsWith("Hiwjcn.Plugin.")).ToList();
        }

        /// <summary>
        /// 注册插件的控制器
        /// </summary>
        protected virtual void RegController(ref ContainerBuilder builder, params Assembly[] ass)
        {
            foreach (var a in ass)
            {
                //注册URL
                builder.RegisterControllers(a);
                //注册插件
                var tps = this.CachedClass(a).Where(x => x.IsAssignableTo_<BasePaymentController>()).ToArray();
                if (ValidateHelper.IsPlumpList(tps))
                {
                    builder.RegisterTypes(tps).As<BasePaymentController>();
                }
            }
        }

        public void Clean()
        {
            this._cache.Clear();
        }
    }
}
