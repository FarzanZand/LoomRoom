const url='https://heyglitch.notion.site/Backbone-944f9e9ce4e54e8bba5ca459fdde14df'; const h=await (await fetch(url)).text(); let i=h.indexOf('<body'); console.log(h.slice(i,i+2000));
