create view ProjectView as 
    select 
        *,
        strftime('%Y-%m-%d %H:%M:%S', a.createdAt, 'julianday') as createdAtString,
        strftime('%Y-%m-%d %H:%M:%S', a.modifiedAt, 'julianday') as modifiedAtString,
        strftime('%Y-%m-%d %H:%M:%S', a.progressDate, 'julianday') as progressDateString,
        strftime('%Y-%m-%d', a.fromDate, 'julianday') as fromDateString,
        strftime('%Y-%m-%d', a.toDate, 'julianday') as toDateString
    from Project a;
