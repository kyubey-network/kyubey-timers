﻿namespace Andoromeda.Kyubey.Timers.Models
{
    public class ApiResult<T>
    {
        public int Code { get; set; }

        public string Msg { get; set; }

        public T Data { get; set; }
    }
}
