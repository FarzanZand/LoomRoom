const urls = [
  'https://heyglitch.notion.site/Button-City-2020-c450f38fb45f4db3a08566ad5187236e',
  'https://heyglitch.notion.site/Beacon-Pines-7d5dfeb5bd8d43c687147acc5163ae2c',
  'https://heyglitch.notion.site/Backbone-944f9e9ce4e54e8bba5ca459fdde14df',
  'https://heyglitch.notion.site/Bear-Breakfast-f3d9ee881d3340f7bc8354ef1c2abe7a'
];
for (const u of urls) { const h=await (await fetch(u)).text(); const arr=[...h.matchAll(/https?:[^"\\ ]+?\.(?:pdf|pptx|png|jpg)(?:\?[^"\\ ]*)?/gi)].map(m=>m[0].replaceAll('&amp;','&')); console.log(u, arr.slice(0,8)); }
