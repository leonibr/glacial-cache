// GlacialCache Landing Page JavaScript
'use strict';

// Documentation data structure
const documentation = [
  {
    icon: '🚀',
    title: 'Getting Started',
    description:
      'Quick start guide with prerequisites, installation, and your first cache operations in ASP.NET Core.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/docs/getting-started.md',
    excerpt:
      'Install GlacialCache.PostgreSQL via NuGet, register it in Program.cs, and start using IDistributedCache for durable, cross-instance caching.',
  },
  {
    icon: '💡',
    title: 'Core Concepts',
    description:
      'Understanding the data model, expiration behavior, cleanup strategy, and how GlacialCache compares to other solutions.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/docs/concepts.md',
    excerpt:
      'Learn about absolute and sliding expiration, PostgreSQL schema design, automatic cleanup, and when to choose GlacialCache.',
  },
  {
    icon: '⚙️',
    title: 'Configuration',
    description:
      'Complete reference for GlacialCachePostgreSQLOptions including connection, cache, maintenance, resilience, and Azure settings.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/docs/configuration.md',
    excerpt:
      'Configure connection pooling, expiration defaults, cleanup intervals, retry policies, circuit breakers, and Azure Managed Identity.',
  },
  {
    icon: '🏗️',
    title: 'Architecture',
    description:
      'System design internals covering components, request flow, background maintenance, and manager election.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/docs/architecture.md',
    excerpt:
      'Deep dive into how GlacialCache handles connection pooling, resilience patterns, background cleanup, and multi-instance coordination.',
  },
  {
    icon: '🔧',
    title: 'Troubleshooting',
    description:
      'Common issues and solutions for connection problems, schema issues, performance tuning, and Azure diagnostics.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/docs/troubleshooting.md',
    excerpt:
      'Diagnose and fix connectivity issues, permission errors, cleanup problems, locking conflicts, and Azure Managed Identity challenges.',
  },
  {
    icon: '📦',
    title: 'Examples',
    description:
      'Runnable code samples showing basic usage, advanced patterns, MemoryPack serialization, and Web API integration.',
    link: 'https://github.com/leonibr/glacial-cache/tree/main/examples',
    excerpt:
      'Explore working examples including console apps, ASP.NET Core Web APIs, custom serializers, and Docker Compose setups.',
  },
];

// Release notes data (placeholder - would typically fetch from GitHub API)
const releases = [
  {
    version: 'v1.0.0',
    date: '2025-01-15',
    description:
      'Initial release of GlacialCache.PostgreSQL with full IDistributedCache support, background cleanup, manager election, and Azure Managed Identity integration.',
    highlights: [
      'Drop-in IDistributedCache implementation',
      'Automatic schema creation and management',
      'Background cleanup with configurable intervals',
      'Manager election for multi-instance deployments',
      'Azure Managed Identity support',
      'Resilience patterns (retry, circuit breaker, timeout)',
      'MemoryPack and JSON serialization options',
      'Comprehensive logging and diagnostics',
    ],
  },
];

// Initialize page
document.addEventListener('DOMContentLoaded', () => {
  // Initialize syntax highlighting
  if (typeof hljs !== 'undefined') {
    hljs.highlightAll();
  }

  // Initialize hero carousel
  initHeroCarousel();

  // Initialize other features
  initDocumentation();
  initReleases();
  initSmoothScroll();
  initMobileMenu();
  initTabs();
  initCodeCopy();
});

// Initialize hero carousel
function initHeroCarousel() {
  // Check if Swiper is available
  if (typeof Swiper === 'undefined') {
    console.warn('Swiper.js not loaded');
    return;
  }

  const heroCarousel = document.querySelector('.hero-carousel');
  if (!heroCarousel) return;

  new Swiper('.hero-carousel', {
    // Carousel settings
    loop: true,
    autoplay: {
      delay: 6000,
      disableOnInteraction: false,
      pauseOnMouseEnter: true,
    },
    speed: 600,
    effect: 'slide',

    // Navigation
    navigation: {
      nextEl: '.swiper-button-next',
      prevEl: '.swiper-button-prev',
    },

    // Pagination
    pagination: {
      el: '.swiper-pagination',
      clickable: true,
      dynamicBullets: false,
    },

    // Responsive breakpoints
    breakpoints: {
      320: {
        slidesPerView: 1,
        spaceBetween: 20,
      },
      768: {
        slidesPerView: 1,
        spaceBetween: 30,
      },
    },

    // Accessibility
    a11y: {
      prevSlideMessage: 'Previous slide',
      nextSlideMessage: 'Next slide',
      paginationBulletMessage: 'Go to slide {{index}}',
    },
  });
}

// Render documentation cards
function initDocumentation() {
  const docContainer = document.getElementById('doc-content');
  if (!docContainer) return;

  const docHTML = documentation
    .map(
      (doc) => `
        <div class="col-lg-4 col-md-6 mb-4 d-flex">
          <div class="doc-card h-100">
              <h3 class="doc-card-title">
                  <span>${doc.icon}</span>
                  ${doc.title}
              </h3>
              <p class="doc-card-description">${doc.description}</p>
              <p class="doc-card-description" style="font-size: 0.875rem; font-style: italic;">${doc.excerpt}</p>
              <a href="${doc.link}" class="doc-card-link" target="_blank" rel="noopener">
                  Read More →
              </a>
          </div>
        </div>
    `
    )
    .join('');

  docContainer.innerHTML = docHTML;
}

// Render release notes
function initReleases() {
  const releasesContainer = document.getElementById('releases-content');
  if (!releasesContainer) return;

  const releasesHTML = releases
    .map((release) => {
      // Parse markdown description if marked is available, otherwise use plain text
      let descriptionHTML = release.description;
      if (typeof marked !== 'undefined' && marked.parse) {
        try {
          descriptionHTML = marked.parse(release.description, {
            breaks: true,
            gfm: true,
          });
        } catch (error) {
          console.warn(
            'Failed to parse markdown for release',
            release.version,
            error
          );
          // Keep original description as fallback
        }
      }

      return `
        <div class="release-card">
            <div class="release-header">
                <span class="release-version">${release.version}</span>
                <span class="release-date">${formatDate(release.date)}</span>
            </div>
            <div class="release-description">${descriptionHTML}</div>
            ${
              release.highlights
                ? `
                <ul style="margin-top: 1rem; padding-left: 1.5rem; color: var(--color-text-light);">
                    ${release.highlights
                      .map(
                        (h) => `<li style="margin-bottom: 0.5rem;">${h}</li>`
                      )
                      .join('')}
                </ul>
            `
                : ''
            }
        </div>
    `;
    })
    .join('');

  releasesContainer.innerHTML = releasesHTML;
}

// Format date helper
function formatDate(dateString) {
  const date = new Date(dateString);
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });
}

// Smooth scroll for anchor links
function initSmoothScroll() {
  document.querySelectorAll('a[href^="#"]').forEach((anchor) => {
    anchor.addEventListener('click', function (e) {
      const href = this.getAttribute('href');
      if (href === '#') return;

      e.preventDefault();
      const target = document.querySelector(href);

      if (target) {
        const headerOffset = 80;
        const elementPosition = target.getBoundingClientRect().top;
        const offsetPosition =
          elementPosition + window.pageYOffset - headerOffset;

        window.scrollTo({
          top: offsetPosition,
          behavior: 'smooth',
        });
      }
    });
  });
}

// Mobile menu toggle (if needed)
function initMobileMenu() {
  // Add mobile menu functionality if nav becomes hamburger menu
  // Currently using flex-wrap for responsive behavior
  const nav = document.querySelector('.nav');
  if (!nav) return;

  // Could add hamburger menu logic here for very small screens
  // For now, CSS flex-wrap handles responsive layout
}

// Initialize tabs for Advanced Features section
function initTabs() {
  const tabButtons = document.querySelectorAll('.tab-button');
  const tabPanels = document.querySelectorAll('.tab-panel');

  if (tabButtons.length === 0 || tabPanels.length === 0) return;

  tabButtons.forEach((button) => {
    button.addEventListener('click', () => {
      const targetTab = button.getAttribute('data-tab');

      // Remove active class from all buttons and panels
      tabButtons.forEach((btn) => {
        btn.classList.remove('active');
        btn.setAttribute('aria-selected', 'false');
      });
      tabPanels.forEach((panel) => {
        panel.classList.remove('active');
      });

      // Add active class to clicked button and corresponding panel
      button.classList.add('active');
      button.setAttribute('aria-selected', 'true');
      const targetPanel = document.getElementById(`tab-${targetTab}`);
      if (targetPanel) {
        targetPanel.classList.add('active');
      }

      // Track tab switch event
      trackEvent('Tab', 'switch', targetTab);
    });
  });
}

// Initialize copy-to-clipboard for code blocks
function initCodeCopy() {
  const codeBlocks = document.querySelectorAll('.code-block, .code-preview');

  codeBlocks.forEach((block) => {
    // Create copy button
    const copyButton = document.createElement('button');
    copyButton.className = 'code-copy-btn';
    copyButton.innerHTML = '📋 Copy';
    copyButton.setAttribute('aria-label', 'Copy code to clipboard');
    copyButton.style.cssText = `
      position: absolute;
      top: 0.5rem;
      right: 0.5rem;
      background: var(--color-primary);
      color: white;
      border: none;
      padding: 0.5rem 1rem;
      border-radius: 0.25rem;
      cursor: pointer;
      font-size: 0.875rem;
      font-weight: 600;
      opacity: 0;
      transition: opacity 0.3s ease;
    `;

    // Make parent relative if not already
    const parent = block.parentElement || block;
    if (getComputedStyle(parent).position === 'static') {
      parent.style.position = 'relative';
    }

    // Show button on hover (with better hover handling)
    let hideTimeout;

    const showButton = () => {
      clearTimeout(hideTimeout);
      copyButton.style.opacity = '1';
    };

    const hideButton = () => {
      clearTimeout(hideTimeout);
      hideTimeout = setTimeout(() => {
        if (!copyButton.classList.contains('copied')) {
          copyButton.style.opacity = '0';
        }
      }, 300);
    };

    block.addEventListener('mouseenter', showButton);
    block.addEventListener('mouseleave', hideButton);
    copyButton.addEventListener('mouseenter', showButton);
    copyButton.addEventListener('mouseleave', hideButton);

    // Copy functionality
    copyButton.addEventListener('click', async () => {
      const codeElement = block.querySelector('code');
      if (!codeElement) return;

      const code = codeElement.textContent || '';

      try {
        await navigator.clipboard.writeText(code);
        copyButton.innerHTML = '✅ Copied!';
        copyButton.classList.add('copied');
        trackEvent('Code', 'copy', 'success');

        setTimeout(() => {
          copyButton.innerHTML = '📋 Copy';
          copyButton.classList.remove('copied');
          copyButton.style.opacity = '0';
        }, 2000);
      } catch (err) {
        console.error('Failed to copy code:', err);
        copyButton.innerHTML = '❌ Failed';
        trackEvent('Code', 'copy', 'error');

        setTimeout(() => {
          copyButton.innerHTML = '📋 Copy';
        }, 2000);
      }
    });

    parent.appendChild(copyButton);
  });
}

// Mobile menu toggle (if needed)
function initMobileMenu() {
  // Add mobile menu functionality if nav becomes hamburger menu
  // Currently using flex-wrap for responsive behavior
  const nav = document.querySelector('.nav');
  if (!nav) return;

  // Could add hamburger menu logic here for very small screens
  // For now, CSS flex-wrap handles responsive layout
}

async function fetchGitHubReleases() {
  try {
    const response = await fetch(
      'https://api.github.com/repos/leonibr/glacial-cache/releases'
    );
    if (!response.ok) throw new Error('Failed to fetch releases');

    const data = await response.json();
    return data.slice(0, 3).map((release) => ({
      version: release.tag_name,
      date: release.published_at.split('T')[0],
      description: release.body || 'No description available.',
      highlights: null,
    }));
  } catch (error) {
    console.warn('Could not fetch GitHub releases:', error);
    throw error; // Let the caller handle the error
  }
}

// Show error message when releases can't be loaded
function showReleasesError() {
  const releasesContainer = document.getElementById('releases-content');
  if (!releasesContainer) return;

  releasesContainer.innerHTML = `
    <div class="release-error-card">
      <div class="error-content">
        <div class="error-icon">
          <i class="fas fa-exclamation-triangle"></i>
        </div>
        <h3 class="error-title">Unable to Load Release Notes</h3>
        <p class="error-message">
          We couldn't load the latest release information right now. Please visit our GitHub releases page to see all updates and changes.
        </p>
        <a
          href="https://github.com/leonibr/glacial-cache/releases"
          class="btn btn-primary"
          target="_blank"
          rel="noopener"
        >
          <i class="fab fa-github me-2"></i>View All Releases on GitHub
        </a>
      </div>
    </div>
  `;
}

// Load real releases from GitHub
(async () => {
  try {
    const liveReleases = await fetchGitHubReleases();
    if (liveReleases.length > 0) {
      releases.length = 0;
      releases.push(...liveReleases);
      initReleases();
    }
  } catch (error) {
    console.warn(
      'Failed to load GitHub releases, showing error message:',
      error
    );
    showReleasesError();
  }
})();

// Analytics tracking (placeholder)
function trackEvent(category, action, label) {
  // Add analytics tracking here (e.g., Google Analytics, Plausible)
  console.log('Event:', category, action, label);
}

// Track CTA clicks
document.addEventListener('click', (e) => {
  const target = e.target.closest('.btn-primary, .btn-secondary');
  if (target) {
    const label = target.textContent.trim();
    trackEvent('CTA', 'click', label);
  }
});

// Intersection Observer for animations (optional enhancement)
function initScrollAnimations() {
  const observerOptions = {
    threshold: 0.1,
    rootMargin: '0px 0px -50px 0px',
  };

  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.style.opacity = '1';
        entry.target.style.transform = 'translateY(0)';
      }
    });
  }, observerOptions);

  // Observe elements for fade-in animations
  document
    .querySelectorAll('.feature-card, .doc-card, .release-card')
    .forEach((el) => {
      el.style.opacity = '0';
      el.style.transform = 'translateY(20px)';
      el.style.transition = 'opacity 0.6s ease, transform 0.6s ease';
      observer.observe(el);
    });
}

// Initialize animations after page load
window.addEventListener('load', () => {
  initScrollAnimations();
});

// Service Worker registration for PWA (optional)
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    // Uncomment to register service worker
    // navigator.serviceWorker.register('/sw.js')
    //     .then(reg => console.log('Service Worker registered'))
    //     .catch(err => console.log('Service Worker registration failed:', err));
  });
}

// Export for potential module usage
if (typeof module !== 'undefined' && module.exports) {
  module.exports = {
    documentation,
    releases,
    fetchGitHubReleases,
  };
}
