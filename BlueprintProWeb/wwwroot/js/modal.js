// Global page loader control
(function(){
    const loader = document.getElementById('page-loader');
    if(!loader) return;

    // Hide CSS spinner if lottie-player is present
    const lottiePlayer = document.getElementById('bpp-loader');
    const spinner = loader.querySelector('.spinner');
    if (lottiePlayer && spinner) {
        // Hide spinner once player is in DOM
        spinner.style.display = 'none';
        // If player errors, show fallback spinner
        lottiePlayer.addEventListener('error', function(){
            spinner.style.display = 'block';
        });
    }

    function hideLoader(){
        if(!loader) return;
        loader.style.opacity = '0';
        loader.style.pointerEvents = 'none';
        setTimeout(()=>{
            if(loader) loader.style.display='none';
        },300);
        document.body.removeAttribute('aria-busy');
    }

    // Show while DOM loading
    document.body.setAttribute('aria-busy','true');

    // Hide after window load to ensure assets fetched
    window.addEventListener('load', hideLoader);

    // Fallback timeout (in case load event missed or long running requests)
    setTimeout(hideLoader, 5000);

    // Expose manual control
    window.BlueprintLoader = {
        show: function(){
            if(!loader) return;
            loader.style.display='flex';
            requestAnimationFrame(()=>{
                loader.style.opacity='1';
                loader.style.pointerEvents='auto';
            });
            document.body.setAttribute('aria-busy','true');
        },
        hide: hideLoader
    };
})();
