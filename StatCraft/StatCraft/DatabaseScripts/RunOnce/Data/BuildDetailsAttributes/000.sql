--compact SortOrders to be dense and 0-indexed
WITH NewOrders as (SELECT Id, ROW_NUMBER() OVER (PARTITION BY BuildNodeId ORDER BY SortOrder) - 1 as SortOrder
	FROM BuildDetailsAttributes)
UPDATE BuildDetailsAttributes as T
	SET SortOrder = S.SortOrder
	FROM NewOrders as S 
	WHERE S.Id = T.Id;